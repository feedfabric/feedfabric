using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Ranking;

// One post served to one viewer in one feed, with the signals the ranker saw at that moment, then what the viewer
// did with it. It is the ranker's training data: positives are posts opened, liked or commented on; negatives are
// posts seen and left. Logging the signals as served keeps training and ranking on the same numbers.
public class FeedLogEntity : EntityBase
{
    public required Guid ViewerId { get; set; }

    public required Guid PostId { get; set; }

    // FeedNames.Following or FeedNames.ForYou
    public required string Feed { get; set; }

    // What ordered the feed: "chronological", "heuristic", or "model:<version>"
    public required string Ranker { get; set; }

    // The ranking profile that built the feed, as "<key>" for a preset or "<key>@<version>"; null for Following
    public string? Profile { get; set; }

    // 0-based place in the feed as served
    public int Position { get; set; }

    public double Score { get; set; }

    // RankingFeatures.Names to values, as JSON
    public required string Features { get; set; }

    public DateTime ServedAt { get; set; }

    // Labels, set as the viewer acts on the post within FeedLog.AttributionWindow of it being served
    public bool Seen { get; set; }

    public bool Opened { get; set; }

    public bool Liked { get; set; }

    public bool Commented { get; set; }

    public int DwellMs { get; set; }

    public bool NotInterested { get; set; }

    // Pressed "Show more": not one of the ranker's goals yet, but kept as a label for when it is
    public bool ShowMore { get; set; }

    // Put in an exploration slot rather than by score, with the chance it had of being picked: these rows are the
    // unbiased data that keeps training from only learning the ranker's own past choices
    public bool Explored { get; set; }

    public double Propensity { get; set; } = 1;
}

public static class FeedNames
{
    public const string Following = "following";
    public const string ForYou = "foryou";
}
