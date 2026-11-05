namespace FeedFabric.Application.Services.Ranking;

// The signals the ranker scores a post on, for one viewer. The order is the model's input order: ml/train_ranker.py
// lists the same names in the same order, and a model trained on a different list is refused (RankerModel).
public static class RankingFeatures
{
    public static readonly string[] Names =
    [
        "age_minutes",
        "follows_author",
        "author_affinity",
        "topic_match",
        "likes",
        "comments",
        "impressions",
        "opens",
        "ctr",
        "engagement_rate",
        "avg_dwell_s",
        "author_followers_log",
        "has_media",
        "content_length",
        "velocity_30m",
        "viewer_author_ctr",
        "viewer_author_seen",
        "session_topic_match",
        "semantic_match",
        // Where the post was in the feed when served (0 = top). Logged as served; always 0 when scoring, so the model
        // learns how much the top spot itself adds and ranks as if every post had it.
        "position",
    ];

    public static int Index(string name) => Array.IndexOf(Names, name);

    public static Dictionary<string, double> ToDictionary(double[] values) =>
        Names.Select((name, i) => (name, Math.Round(values[i], 4))).ToDictionary(x => x.name, x => x.Item2);
}

// A post's signals and score, with the reasons in words for "Why this post?"
public sealed record RankedPost(Guid PostId, double Score, double[] Features, IReadOnlyList<RankingFactor> Factors)
{
    // Put in an exploration slot rather than earned by score, and the chance it had of being picked
    public bool Explored { get; init; }

    public double Propensity { get; init; } = 1;
}

public sealed record RankingFactor(string Label, double Value, string Detail);
