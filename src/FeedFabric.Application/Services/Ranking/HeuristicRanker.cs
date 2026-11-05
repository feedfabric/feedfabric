namespace FeedFabric.Application.Services.Ranking;

// The scoring formula: what the feed uses until a trained model exists, and the baseline a model has to beat.
// score = recency × interest × quality, each part 1 or more so no single one can zero a post out:
//   recency  halves about every 2¾ hours
//   interest grows with how much you engage with the post's topics, author and meaning, what you're into this
//            session, and whether you follow them
//   quality  grows with how often people who saw it opened, liked or commented, how long they stayed, and how fast
//            it's picking up engagement right now
// ml/train_ranker.py has the same formula, to compare models against; keep the two in step.
public static class HeuristicRanker
{
    private static readonly int Age = RankingFeatures.Index("age_minutes");
    private static readonly int Follows = RankingFeatures.Index("follows_author");
    private static readonly int Affinity = RankingFeatures.Index("author_affinity");
    private static readonly int Topics = RankingFeatures.Index("topic_match");
    private static readonly int Ctr = RankingFeatures.Index("ctr");
    private static readonly int Engagement = RankingFeatures.Index("engagement_rate");
    private static readonly int Dwell = RankingFeatures.Index("avg_dwell_s");
    private static readonly int Velocity = RankingFeatures.Index("velocity_30m");
    private static readonly int Session = RankingFeatures.Index("session_topic_match");
    private static readonly int Semantic = RankingFeatures.Index("semantic_match");

    public static double Score(double[] f) => Factors(f).Aggregate(1.0, (score, factor) => score * factor.Value);

    // Each part of the score, for "Why this post?"
    public static IReadOnlyList<RankingFactor> Factors(double[] f)
    {
        var recency = Math.Exp(-f[Age] / 240);
        var topics = 3 * f[Topics];
        var author = 0.6 * Math.Log(1 + Math.Max(0, f[Affinity]));
        var follows = 0.4 * f[Follows];
        var meaning = 1.5 * Math.Max(0, f[Semantic] - 0.2);
        var session = 2 * f[Session];
        var popular = 8 * f[Engagement] + 4 * f[Ctr];
        var trending = 0.15 * Math.Log(1 + f[Velocity]);
        var attention = 0.05 * Math.Min(f[Dwell], 20);

        return
        [
            new("Recent", recency, f[Age] < 60 ? $"{f[Age]:0} minutes old" : $"{f[Age] / 60:0.#} hours old"),
            new("Your topics", 1 + topics, $"{f[Topics]:P0} match with what you engage with"),
            new("Similar to what you like", 1 + meaning, $"{Math.Max(0, f[Semantic]):P0} similar in meaning to your recent likes"),
            new("This session", 1 + session, $"{f[Session]:P0} match with what you've just engaged with"),
            new("Author you like", 1 + author, $"{f[Affinity]:0} points from your likes and comments"),
            new("You follow them", 1 + follows, f[Follows] > 0 ? "Yes" : "No"),
            new("Popular", 1 + popular, $"{f[Engagement]:P1} engagement, {f[Ctr]:P1} opens"),
            new("Trending now", 1 + trending, $"{f[Velocity]:0} likes and comments in the last 30 minutes"),
            new("Holds attention", 1 + attention, $"{f[Dwell]:0.#}s average on screen"),
        ];
    }
}
