using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Ranking.Profiles;

public class RankingOptions
{
    public const string Section = "Ranking";

    // The profile For You uses when a request doesn't name one: a preset key or a saved profile's key
    public string DefaultProfile { get; set; } = RankingPresets.Balanced;

    // Serve a ranker trained on the simulator's behaviour ("synthetic" or "all" in latest.json). True in
    // Development and in the simulator; elsewhere such a model is refused and For You keeps the formula, since it
    // learned what simulated personas like, not what members do.
    public bool AllowSimulatedRanker { get; set; }
}

// A profile ready to rank with: its settings, its formula parsed, and the label FeedLog records
public sealed record ResolvedProfile(string Key, int Version, RankingProfile Settings, ScoreExpression? Expression)
{
    public bool IsPreset => Version == 0;

    // Drafts (version -1) are only previewed, never logged
    public string Label => Version > 0 ? $"{Key}@{Version}" : Key;
}

public sealed record ProfileSummary(string Key, string Name, string Description, bool Preset, int Version, DateTime? SavedAt, bool IsDefault);

public sealed record ProfileVersion(int Version, DateTime SavedAt, string? SavedBy);

public enum ProfileSaveStatus
{
    Ok,
    Invalid,
    InvalidKey,
    ReadOnly,
}

public sealed record ProfileSaveResult(ProfileSaveStatus Status, ResolvedProfile? Profile = null, IReadOnlyList<ProfileError>? Errors = null);

// Presets from code, custom profiles from the database (every saved version kept)
public partial class RankingProfileStore(AppDbContext db, IOptions<RankingOptions> options, TimeProvider timeProvider)
{
    public const int MaxKeyLength = 40;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string DefaultKey => options.Value.DefaultProfile;

    // A request's profile, or the default; null when a named profile doesn't exist
    public async Task<ResolvedProfile?> ResolveAsync(string? key, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            return await GetAsync(key, cancellationToken);
        }
        // A default that's been deleted falls back to the built-in one rather than breaking every feed
        return await GetAsync(DefaultKey, cancellationToken) ?? Compile(RankingPresets.Balanced, 0, RankingPresets.All[RankingPresets.Balanced]());
    }

    public async Task<ResolvedProfile?> GetAsync(string key, CancellationToken cancellationToken)
    {
        key = key.Trim().ToLowerInvariant();
        if (RankingPresets.All.TryGetValue(key, out var preset))
        {
            return Compile(key, 0, preset());
        }

        var row = await db.RankingProfiles.AsNoTracking().Where(p => p.Key == key)
            .OrderByDescending(p => p.Version).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<ResolvedProfile?> GetVersionAsync(string key, int version, CancellationToken cancellationToken)
    {
        var row = await db.RankingProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Key == key && p.Version == version, cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<List<ProfileSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var presets = RankingPresets.All.Select(p =>
        {
            var settings = p.Value();
            return new ProfileSummary(p.Key, settings.Name, settings.Description, true, 0, null, p.Key == DefaultKey);
        });

        var latest = await db.RankingProfiles.AsNoTracking()
            .Where(p => p.Version == db.RankingProfiles.Where(q => q.Key == p.Key).Max(q => q.Version))
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken);
        var custom = latest.Select(row =>
        {
            var settings = Deserialize(row.Settings);
            return new ProfileSummary(row.Key, settings.Name, settings.Description, false, row.Version, row.SavedAt, row.Key == DefaultKey);
        });

        return presets.Concat(custom).ToList();
    }

    public async Task<List<ProfileVersion>> VersionsAsync(string key, CancellationToken cancellationToken) =>
        await db.RankingProfiles.AsNoTracking().Where(p => p.Key == key)
            .OrderByDescending(p => p.Version)
            .Select(p => new ProfileVersion(p.Version, p.SavedAt, p.SavedBy))
            .ToListAsync(cancellationToken);

    // Saves a new version; feeds pick it up on their next first page
    public async Task<ProfileSaveResult> SaveAsync(string key, RankingProfile profile, string savedBy, CancellationToken cancellationToken)
    {
        key = key.Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(key))
        {
            return new ProfileSaveResult(ProfileSaveStatus.InvalidKey);
        }
        if (RankingPresets.IsPreset(key))
        {
            return new ProfileSaveResult(ProfileSaveStatus.ReadOnly);
        }

        profile.Normalised();
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            return new ProfileSaveResult(ProfileSaveStatus.Invalid, Errors: errors);
        }

        // Two saves at once both try the next version; the unique index lets one win, and the other tries again
        for (var attempt = 0; ; attempt++)
        {
            var version = (await db.RankingProfiles.Where(p => p.Key == key).MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0) + 1;
            var row = new RankingProfileEntity
            {
                Key = key,
                Version = version,
                Settings = JsonSerializer.Serialize(profile, JsonOptions),
                SavedAt = timeProvider.GetUtcNow().UtcDateTime,
                SavedBy = savedBy,
            };
            db.RankingProfiles.Add(row);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new ProfileSaveResult(ProfileSaveStatus.Ok, FromRow(row));
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.Entry(row).State = EntityState.Detached;
            }
        }
    }

    // Removes every version. Feed log rows keep the label, so history still says what served them.
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        await db.RankingProfiles.Where(p => p.Key == key).ExecuteDeleteAsync(cancellationToken) > 0;

    // Checks a draft and gets it ready to rank with, for previews; null with errors when it isn't valid
    public static ResolvedProfile? Draft(RankingProfile profile, out List<ProfileError> errors)
    {
        profile.Normalised();
        errors = profile.Validate().Where(e => e.Field != "name").ToList();
        return errors.Count > 0 ? null : Compile("draft", -1, profile);
    }

    public static ResolvedProfile Compile(string key, int version, RankingProfile settings)
    {
        ScoreExpression? expression = null;
        if (settings.Scoring.Mode == ScoringMode.Expression)
        {
            ScoreExpression.TryParse(settings.Scoring.Expression ?? "", out expression, out _);
        }
        return new ResolvedProfile(key, version, settings, expression);
    }

    private static ResolvedProfile FromRow(RankingProfileEntity row) => Compile(row.Key, row.Version, Deserialize(row.Settings));

    private static RankingProfile Deserialize(string json) => JsonSerializer.Deserialize<RankingProfile>(json, JsonOptions) ?? new RankingProfile();

    // Lowercase letters, digits and dashes, starting with a letter: it goes in URLs and the feed log
    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$")]
    private static partial Regex KeyPattern();
}
