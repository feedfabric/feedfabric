using FeedFabric.Application.Services.Ranking;

namespace FeedFabric.Application.Tests.Ranking;

public class DiversityRerankerTests
{
    private static DiversityReranker.Candidate Post(int rank, Guid author, string topic, bool inNetwork = true) =>
        new(new RankedPost(Guid.NewGuid(), 1000 - rank, new double[RankingFeatures.Names.Length], []), author, topic, inNetwork);

    [Fact]
    public void No_author_appears_more_than_twice_in_any_ten()
    {
        var prolific = Guid.NewGuid();
        // The 30 best posts are all one author's; others follow
        var candidates = Enumerable.Range(0, 30).Select(i => Post(i, prolific, $"t{i % 3}"))
            .Concat(Enumerable.Range(30, 60).Select(i => Post(i, Guid.NewGuid(), $"t{i % 3}")))
            .ToList();
        var authorOf = candidates.ToDictionary(c => c.Post.PostId, c => c.AuthorId);

        var feed = DiversityReranker.Arrange(candidates, new Random(1));

        for (var start = 0; start + 10 <= 40; start++)
        {
            Assert.True(feed.Skip(start).Take(10).Count(p => authorOf[p.PostId] == prolific) <= 2);
        }
        Assert.Equal(candidates.Count, feed.Count);
    }

    [Fact]
    public void No_more_than_two_posts_on_one_topic_in_a_row_while_others_are_available()
    {
        var candidates = Enumerable.Range(0, 20).Select(i => Post(i, Guid.NewGuid(), "running"))
            .Concat(Enumerable.Range(20, 20).Select(i => Post(i, Guid.NewGuid(), "yoga")))
            .ToList();
        var topicOf = candidates.ToDictionary(c => c.Post.PostId, c => c.Topic);

        var feed = DiversityReranker.Arrange(candidates, new Random(1));

        for (var i = 2; i < 20; i++)
        {
            Assert.False(topicOf[feed[i].PostId] == topicOf[feed[i - 1].PostId] && topicOf[feed[i - 1].PostId] == topicOf[feed[i - 2].PostId]);
        }
    }

    [Fact]
    public void Each_ten_has_an_exploration_slot_from_below_the_top_thirty_with_its_odds()
    {
        var candidates = Enumerable.Range(0, 100).Select(i => Post(i, Guid.NewGuid(), $"t{i % 5}", inNetwork: i % 2 == 0)).ToList();
        var rankOf = candidates.Select((c, i) => (c.Post.PostId, i)).ToDictionary(x => x.PostId, x => x.i);

        var feed = DiversityReranker.Arrange(candidates, new Random(1));

        var explored = feed.Select((p, i) => (p, i)).Where(x => x.p.Explored).ToList();
        Assert.Contains(explored, x => x.i == 6);
        Assert.All(explored, x => Assert.True(rankOf[x.p.PostId] >= DiversityRules.Default.ExploreFrom));
        Assert.All(explored, x => Assert.InRange(x.p.Propensity, 0.0001, 1));
    }

    [Fact]
    public void Each_ten_includes_posts_from_outside_your_follows_when_there_are_any()
    {
        var candidates = Enumerable.Range(0, 40).Select(i => Post(i, Guid.NewGuid(), $"t{i % 5}", inNetwork: true))
            .Concat(Enumerable.Range(40, 10).Select(i => Post(i, Guid.NewGuid(), $"t{i % 5}", inNetwork: false)))
            .ToList();
        var inNetwork = candidates.ToDictionary(c => c.Post.PostId, c => c.InNetwork);

        var feed = DiversityReranker.Arrange(candidates, new Random(1));

        Assert.True(feed.Take(10).Count(p => !inNetwork[p.PostId]) >= DiversityRules.Default.OutOfNetwork);
    }

    [Fact]
    public void A_profile_can_allow_one_post_per_author_and_turn_exploration_off()
    {
        var prolific = Guid.NewGuid();
        var candidates = Enumerable.Range(0, 10).Select(i => Post(i, prolific, $"t{i % 3}"))
            .Concat(Enumerable.Range(10, 60).Select(i => Post(i, Guid.NewGuid(), $"t{i % 3}")))
            .ToList();
        var authorOf = candidates.ToDictionary(c => c.Post.PostId, c => c.AuthorId);

        var feed = DiversityReranker.Arrange(candidates, new Random(1), new DiversityRules(AuthorCap: 1, Exploration: 0));

        Assert.Equal(1, feed.Take(10).Count(p => authorOf[p.PostId] == prolific));
        Assert.DoesNotContain(feed, p => p.Explored);
    }

    [Fact]
    public void Two_exploration_slots_sit_at_three_and_seven()
    {
        var candidates = Enumerable.Range(0, 100).Select(i => Post(i, Guid.NewGuid(), $"t{i % 5}")).ToList();

        var feed = DiversityReranker.Arrange(candidates, new Random(1), new DiversityRules(Exploration: 2, OutOfNetwork: 0));

        Assert.Equal([3, 7], feed.Take(10).Select((p, i) => (p, i)).Where(x => x.p.Explored).Select(x => x.i));
    }
}
