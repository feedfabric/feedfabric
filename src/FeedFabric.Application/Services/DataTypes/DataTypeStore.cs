using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FeedFabric.Application.Services.DataTypes;

public enum DataTypeSaveStatus
{
    Ok,
    InvalidKey,
    // Not JSON, not a schema, or its root doesn't describe an object; DataTypeSaveResult.Message says which
    InvalidSchema,
}

public sealed record DataTypeSaveResult(DataTypeSaveStatus Status, DataTypeResponse? Type = null, string? Message = null);

public enum DataCheckStatus
{
    Ok,
    UnknownType,
    TooLarge,
    Invalid,
}

public sealed record DataCheckResult(DataCheckStatus Status, int Version = 0, string? Json = null, IReadOnlyList<DataError>? Errors = null);

// The data types posts can carry, and checking a post's data against its type's schema. Schemas never fetch anything:
// a $ref can only point inside the schema itself.
public sealed partial class DataTypeStore(AppDbContext db, TimeProvider timeProvider, IMemoryCache cache)
{
    public const int MaxKeyLength = 40;
    public const int MaxLabelLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxSchemaBytes = 32 * 1024;

    // A post's data, as JSON
    public const int MaxDataBytes = 16 * 1024;

    // So the post's errors stay readable when a lot is wrong
    private const int MaxErrors = 20;

    private static readonly EvaluationOptions Evaluation = new() { OutputFormat = OutputFormat.List };

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    public async Task<List<DataTypeResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var types = await db.DataTypes.AsNoTracking().OrderBy(t => t.Key).ToListAsync(cancellationToken);
        return [.. types.Select(DataTypeResponse.From)];
    }

    public async Task<DataTypeResponse?> GetAsync(string key, CancellationToken cancellationToken) =>
        await db.DataTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Key == key, cancellationToken) is { } type ? DataTypeResponse.From(type) : null;

    // Creates the type, or saves a new version of it. Posts already made keep the data they have.
    public async Task<DataTypeSaveResult> SaveAsync(string key, string label, string? description, JsonElement schema, string savedBy,
        CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
        {
            return new DataTypeSaveResult(DataTypeSaveStatus.InvalidKey);
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return new DataTypeSaveResult(DataTypeSaveStatus.InvalidSchema, Message: "The schema must be a JSON object.");
        }
        var json = schema.GetRawText();
        if (Problem(json) is { } problem)
        {
            return new DataTypeSaveResult(DataTypeSaveStatus.InvalidSchema, Message: problem);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var type = await db.DataTypes.SingleOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (type is null)
        {
            type = new DataTypeEntity { Key = key, Label = label.Trim(), Schema = json, Version = 1 };
            db.DataTypes.Add(type);
        }
        else
        {
            type.Label = label.Trim();
            // jsonb keeps its own spacing and key order, so compare the JSON rather than the text
            type.Version += JsonNode.DeepEquals(JsonNode.Parse(type.Schema), JsonNode.Parse(json)) ? 0 : 1;
            type.Schema = json;
        }
        type.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        type.UpdatedAt = now;
        type.UpdatedBy = savedBy;
        await db.SaveChangesAsync(cancellationToken);
        return new DataTypeSaveResult(DataTypeSaveStatus.Ok, DataTypeResponse.From(type));
    }

    // Posts that already have this type keep their data; new ones can't use it
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        await db.DataTypes.Where(t => t.Key == key).ExecuteDeleteAsync(cancellationToken) > 0;

    // Whether a post's data matches its type's current schema
    public async Task<DataCheckResult> CheckAsync(string typeKey, JsonElement value, CancellationToken cancellationToken)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            return new DataCheckResult(DataCheckStatus.Invalid, Errors: [new DataError("", "Send the data in data.value.")]);
        }
        var json = value.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaxDataBytes)
        {
            return new DataCheckResult(DataCheckStatus.TooLarge);
        }
        var type = await db.DataTypes.AsNoTracking().Where(t => t.Key == typeKey).Select(t => new { t.Version, t.Schema })
            .SingleOrDefaultAsync(cancellationToken);
        if (type is null)
        {
            return new DataCheckResult(DataCheckStatus.UnknownType);
        }

        // Built once per version
        var schema = cache.GetOrCreate($"datatype:{typeKey}:{type.Version}", entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            return Build(type.Schema);
        })!;
        var results = schema.Evaluate(value, Evaluation);
        if (results.IsValid)
        {
            return new DataCheckResult(DataCheckStatus.Ok, type.Version, json);
        }
        var errors = (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Values.Select(message => new DataError(d.InstanceLocation.ToString(), message)))
            .Distinct()
            .Take(MaxErrors)
            .ToList();
        return new DataCheckResult(DataCheckStatus.Invalid, Errors: errors.Count > 0 ? errors : [new DataError("", "Doesn't match the schema.")]);
    }

    // Why this can't be a data type's schema, or null if it can
    private static string? Problem(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxSchemaBytes)
        {
            return $"The schema is over {MaxSchemaBytes / 1024} KB.";
        }
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("type", out var rootType) || rootType.ValueKind != JsonValueKind.String
            || rootType.GetString() != "object")
        {
            return "The schema's root must be \"type\": \"object\", so a post's data is always an object.";
        }
        try
        {
            Build(json);
            return null;
        }
        catch (Exception e) when (e is JsonSchemaException or JsonException or ArgumentException or InvalidOperationException)
        {
            return e.Message;
        }
    }

    // Its own registry each time, so two types with the same $id don't collide and nothing is looked up remotely
    private static JsonSchema Build(string json) => JsonSchema.FromText(json, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
}
