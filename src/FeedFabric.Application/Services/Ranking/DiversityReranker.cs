using FeedFabric.Application.Services.Ranking.Profiles;

namespace FeedFabric.Application.Services.Ranking;

// Turns a list sorted by score into a feed, as large feeds do after ranking. Scores rank posts one at a time;
// a feed is read as a whole, so it also needs (each set by the profile, DiversityRules):
//   - at most AuthorCap posts by one author in any Window
//   - no more than TopicRun posts on the same main topic in a row
//   - at least OutOfNetwork posts from accounts you don't follow in each Window, when there are any
//   - ExploreSlots in each Window: a random post from below ExploreFrom that also keeps to the author and topic
//     rules, logged with the chance it had of being picked, so training sees posts the ranker wouldn't have shown
// Each rule gives way when no remaining post can meet it, so the feed never comes up short.
public static class DiversityReranker
{
    public sealed record Candidate(RankedPost Post, Guid AuthorId, string? Topic, bool InNetwork);

    public static List<RankedPost> Arrange(IReadOnlyList<Candidate> byScore, Random random) =>
        Arrange(byScore, random, DiversityRules.Default);

    public static List<RankedPost> Arrange(IReadOnlyList<Candidate> byScore, Random random, DiversityRules rules)
    {
        var window = DiversityRules.Window;
        var exploreSlots = rules.ExploreSlots();
        var remaining = byScore.Select((c, rank) => (Candidate: c, Rank: rank)).ToList();
        var feed = new List<Candidate>(byScore.Count);
        var explored = new HashSet<Guid>();
        var propensities = new Dictionary<Guid, double>();
        // What the rules look back at, worked out once per slot rather than for every candidate tried in it
        var authorCounts = new Dictionary<Guid, int>();
        var lastTopics = new List<string?>(Math.Max(0, rules.TopicRun));
        var pool = new List<int>();

        while (remaining.Count > 0)
        {
            var slot = feed.Count % window;
            var recentStart = Math.Max(0, feed.Count - (window - 1));
            authorCounts.Clear();
            for (var i = recentStart; i < feed.Count; i++)
            {
                authorCounts[feed[i].AuthorId] = authorCounts.GetValueOrDefault(feed[i].AuthorId) + 1;
            }
            lastTopics.Clear();
            for (var i = Math.Max(recentStart, feed.Count - rules.TopicRun); i < feed.Count; i++)
            {
                lastTopics.Add(feed[i].Topic);
            }

            bool Fits(Candidate candidate) =>
                authorCounts.GetValueOrDefault(candidate.AuthorId) < rules.AuthorCap && FitsTopic(candidate, lastTopics, rules);

            var pick = -1;

            if (exploreSlots.Contains(slot))
            {
                pool.Clear();
                for (var i = 0; i < remaining.Count; i++)
                {
                    if (remaining[i].Rank >= rules.ExploreFrom && Fits(remaining[i].Candidate))
                    {
                        pool.Add(i);
                    }
                }
                if (pool.Count > 0)
                {
                    pick = pool[random.Next(pool.Count)];
                    var postId = remaining[pick].Candidate.Post.PostId;
                    explored.Add(postId);
                    propensities[postId] = 1.0 / pool.Count;
                }
            }

            // Running short of posts from outside your follows in this window: take the best one that fits
            var outOfNetworkShown = 0;
            for (var i = feed.Count - slot; i < feed.Count; i++)
            {
                outOfNetworkShown += feed[i].InNetwork ? 0 : 1;
            }
            var slotsLeft = window - slot;
            var needOutside = rules.OutOfNetwork > 0 && rules.OutOfNetwork - outOfNetworkShown >= slotsLeft;

            if (pick < 0 && needOutside)
            {
                pick = remaining.FindIndex(r => !r.Candidate.InNetwork && Fits(r.Candidate));
            }
            if (pick < 0)
            {
                pick = remaining.FindIndex(r => Fits(r.Candidate));
            }
            if (pick < 0)
            {
                pick = 0;
            }

            feed.Add(remaining[pick].Candidate);
            remaining.RemoveAt(pick);
        }

        return feed.Select(c => explored.Contains(c.Post.PostId)
            ? c.Post with { Explored = true, Propensity = propensities[c.Post.PostId] }
            : c.Post).ToList();
    }

    // No more than TopicRun in a row on one topic: blocked only when the last TopicRun posts are all on this one
    private static bool FitsTopic(Candidate candidate, List<string?> lastTopics, DiversityRules rules)
    {
        if (candidate.Topic is null || lastTopics.Count < rules.TopicRun)
        {
            return true;
        }
        foreach (var topic in lastTopics)
        {
            if (topic != candidate.Topic)
            {
                return true;
            }
        }
        return false;
    }
}

public sealed record DiversityRules(int AuthorCap = 2, int TopicRun = 2, int OutOfNetwork = 2, int Exploration = 1, int ExploreFrom = 30)
{
    public const int Window = 10;

    public static readonly DiversityRules Default = new();

    public static DiversityRules From(RankingProfile profile) => new(
        profile.Variety.AuthorCap, profile.Variety.TopicRun, profile.Variety.OutOfNetwork, profile.Exploration.Slots, profile.Exploration.FromRank);

    // Where the exploration slots sit in each ten, spread out and clear of the top: 1 → 6; 2 → 3, 7; 3 → 2, 5, 8
    public HashSet<int> ExploreSlots() => Exploration switch
    {
        <= 0 => [],
        1 => [6],
        2 => [3, 7],
        _ => [2, 5, 8],
    };
}
