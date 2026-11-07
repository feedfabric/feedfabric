namespace FeedFabric.Application.Services.Ranking.Profiles;

// One saved version of a custom ranking profile. Saving adds a row rather than changing one, so every version a
// feed was served with stays readable; FeedLog names it as "<key>@<version>".
public class RankingProfileEntity : EntityBase
{
    public required string Key { get; init; }

    public int Version { get; init; }

    // RankingProfile as JSON
    public required string Settings { get; init; }

    public DateTime SavedAt { get; init; }

    // The external user ID of whoever saved it
    public string? SavedBy { get; init; }
}
