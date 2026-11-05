namespace FeedFabric.Application.Services.Ranking.Profiles;

// One saved version of a custom ranking profile. Saving adds a row rather than changing one, so every version a
// feed was served with stays readable; FeedLog names it as "<key>@<version>".
public class RankingProfileEntity : EntityBase
{
    public required string Key { get; set; }

    public int Version { get; set; }

    // RankingProfile as JSON
    public required string Settings { get; set; }

    public DateTime SavedAt { get; set; }

    // The external user ID of whoever saved it
    public string? SavedBy { get; set; }
}
