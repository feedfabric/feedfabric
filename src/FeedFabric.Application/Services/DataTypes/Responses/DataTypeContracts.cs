using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedFabric.Application.Services.DataTypes;

public sealed class DataTypeResponse
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }

    public int Version { get; init; }

    // The JSON Schema, as saved; feed it to a type generator for the SDK's typed helpers
    [JsonConverter(typeof(RawJsonConverter))]
    public required string Schema { get; init; }

    public DateTime UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }

    public static DataTypeResponse From(DataTypeEntity type) => new()
    {
        Key = type.Key, Label = type.Label, Description = type.Description, Version = type.Version, Schema = type.Schema,
        UpdatedAt = type.UpdatedAt, UpdatedBy = type.UpdatedBy,
    };
}

// A post's structured data, as feeds return it
public sealed class PostDataResponse
{
    public required string Type { get; init; }

    // The data type's version it was checked against
    public int Version { get; init; }

    [JsonConverter(typeof(RawJsonConverter))]
    public required string Value { get; init; }
}

// One way the data doesn't match its schema: where (a JSON Pointer into the data, "" for the whole of it) and why
public sealed record DataError(string Path, string Message);

// JSON kept as text in the database, written out as JSON rather than as a string
public sealed class RawJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteRawValue(value);
}
