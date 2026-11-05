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

        while (remaining.Count > 0)
        {
            var slot = feed.Count % window;
            var recent = feed.Skip(Math.Max(0, feed.Count - (window - 1))).ToList();
            var thisWindow = feed.Skip(feed.Count - slot).ToList();

            (Candidate Candidate, int Rank)? pick = null;

            if (exploreSlots.Contains(slot))
            {
                var pool = remaining.Where(r => r.Rank >= rules.ExploreFrom && Fits(r.Candidate, recent, rules)).ToList();
                if (pool.Count > 0)
                {
                    pick = pool[random.Next(pool.Count)];
                    explored.Add(pick.Value.Candidate.Post.PostId);
                    propensities[pick.Value.Candidate.Post.PostId] = 1.0 / pool.Count;
                }
            }

            // Running short of posts from outside your follows in this window: take the best one that fits
            var outOfNetworkShown = thisWindow.Count(c => !c.InNetwork);
            var slotsLeft = window - slot;
            var needOutside = rules.OutOfNetwork > 0 && rules.OutOfNetwork - outOfNetworkShown >= slotsLeft;

            pick ??= remaining.Where(r => (!needOutside || !r.Candidate.InNetwork) && Fits(r.Candidate, recent, rules)).Cast<(Candidate, int)?>().FirstOrDefault()
                ?? remaining.Where(r => Fits(r.Candidate, recent, rules)).Cast<(Candidate, int)?>().FirstOrDefault()
                ?? remaining[0];

            remaining.Remove(pick.Value);
            feed.Add(pick.Value.Candidate);
        }

        return feed.Select(c => explored.Contains(c.Post.PostId)
            ? c.Post with { Explored = true, Propensity = propensities[c.Post.PostId] }
            : c.Post).ToList();
    }

    private static bool Fits(Candidate candidate, List<Candidate> recent, DiversityRules rules)
    {
        if (recent.Count(c => c.AuthorId == candidate.AuthorId) >= rules.AuthorCap)
        {
            return false;
        }
        var lastTopics = recent.TakeLast(rules.TopicRun).Select(c => c.Topic).ToList();
        return candidate.Topic is null || lastTopics.Count < rules.TopicRun || lastTopics.Any(t => t != candidate.Topic);
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
