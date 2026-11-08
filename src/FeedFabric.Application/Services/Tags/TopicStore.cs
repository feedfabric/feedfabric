using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FeedFabric.Application.Services.Tags;

public sealed record TopicVersion(int Version, DateTime SavedAt, string? SavedBy, string? Library, int Topics);

public enum TopicSaveStatus
{
    Ok,
    Invalid,
    UnknownLibrary,
}

public sealed record TopicSaveResult(TopicSaveStatus Status, CurrentTopics? Topics = null, IReadOnlyList<TopicError>? Errors = null);

// The community's topics in the database, every saved version kept. New posts use a saved set within a minute on
// every instance (TopicRules); posts already tagged keep their tags.
public class TopicStore(AppDbContext db, TopicRules rules, TimeProvider timeProvider)
{
    public const int MaxLibraryLength = 40;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CurrentTopics> CurrentAsync(CancellationToken cancellationToken) => rules.CurrentAsync(cancellationToken);

    public async Task<List<TopicVersion>> VersionsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.TopicSets.AsNoTracking().OrderByDescending(t => t.Version).ToListAsync(cancellationToken);
        return [.. rows.Select(t => new TopicVersion(t.Version, t.SavedAt, t.SavedBy, t.Library, Deserialize(t.Topics).Count))];
    }

    public async Task<Dictionary<string, List<string>>?> VersionAsync(int version, CancellationToken cancellationToken) =>
        await db.TopicSets.AsNoTracking().Where(t => t.Version == version).Select(t => t.Topics).SingleOrDefaultAsync(cancellationToken)
            is { } json ? Deserialize(json) : null;

    // Saves a new version and makes it the one in use
    public async Task<TopicSaveResult> SaveAsync(IReadOnlyDictionary<string, List<string>> topics, string? library, string savedBy,
        CancellationToken cancellationToken)
    {
        if (library is not null && TopicLibrary.Find(library) is null)
        {
            return new TopicSaveResult(TopicSaveStatus.UnknownLibrary);
        }
        var errors = TopicMatcher.Problems(topics);
        if (errors.Count > 0)
        {
            return new TopicSaveResult(TopicSaveStatus.Invalid, Errors: errors);
        }

        var normalised = TopicMatcher.Normalise(topics);
        // Two saves at once both try the next version; the unique index lets one win, and the other tries again
        for (var attempt = 0; ; attempt++)
        {
            var version = (await db.TopicSets.MaxAsync(t => (int?)t.Version, cancellationToken) ?? 0) + 1;
            var row = new TopicSetEntity
            {
                Version = version,
                Topics = JsonSerializer.Serialize(normalised, JsonOptions),
                Library = library?.ToLowerInvariant(),
                SavedAt = timeProvider.GetUtcNow().UtcDateTime,
                SavedBy = savedBy,
            };
            db.TopicSets.Add(row);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                rules.Changed();
                return new TopicSaveResult(TopicSaveStatus.Ok, await rules.CurrentAsync(cancellationToken));
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.Entry(row).State = EntityState.Detached;
            }
        }
    }

    public static Dictionary<string, List<string>> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions) ?? [];
}
