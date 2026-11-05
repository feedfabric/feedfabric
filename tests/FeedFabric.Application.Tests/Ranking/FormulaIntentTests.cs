using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Tests.Ranking;

// Each studio example and preset formula does what its label says, on posts that differ in just the one thing it
// should care about; and the rules that apply a profile's boosts and filters behave on any score
public class FormulaIntentTests
{
    // A post as the formula sees it: signals by name, plus the built-in formula's and the model's scores
    private sealed record Post(double Formula = 2, double Model = 1, double AgeHours = 1, double Likes = 0, double Comments = 0,
        double TopicMatch = 0, double Velocity = 0);

    private static double Score(string text, Post post)
    {
        Assert.True(ScoreExpression.TryParse(text, out var expression, out var error), error?.Message);
        var values = new double[ScoreExpression.Width];
        values[RankingFeatures.Index("age_minutes")] = post.AgeHours * 60;
        values[RankingFeatures.Index("likes")] = post.Likes;
        values[RankingFeatures.Index("comments")] = post.Comments;
        values[RankingFeatures.Index("topic_match")] = post.TopicMatch;
        values[RankingFeatures.Index("velocity_30m")] = post.Velocity;
        values[ScoreExpression.IndexOf("age_hours")] = post.AgeHours;
        values[ScoreExpression.IndexOf("formula")] = post.Formula;
        values[ScoreExpression.IndexOf("model")] = post.Model;
        return expression!.Evaluate(values);
    }

    private static string Example(string label) => ScoreExpression.Examples.Single(e => e.Label == label).Text;

    private static readonly string FreshPreset = RankingPresets.All["fresh"]().Scoring.Expression!;

    [Fact]
    public void Model_fading_over_12_hours_halves_every_12_hours_and_keeps_the_models_order()
    {
        var formula = Example("Model, fading over 12 hours");
        Assert.Equal(0.5, Score(formula, new Post(Model: 1, AgeHours: 13)) / Score(formula, new Post(Model: 1, AgeHours: 1)), 6);
        Assert.True(Score(formula, new Post(Model: 2)) > Score(formula, new Post(Model: 1)));
        // A post twice as good, 12 hours older, ties
        Assert.Equal(Score(formula, new Post(Model: 1, AgeHours: 0)), Score(formula, new Post(Model: 2, AgeHours: 12)), 6);
    }

    [Fact]
    public void Fading_never_lifts_an_old_post_the_model_expects_to_be_skipped()
    {
        // The model's value goes below 0 when a skip is likely; "model" in a formula is shifted to 0 or more
        var goals = new Goals();
        var model = ProfileRules.NonNegativeModel([-0.3, -0.3, 0.8], goals);
        var formula = Example("Model, fading over 12 hours");

        var newBad = Score(formula, new Post(Model: model[0], AgeHours: 0));
        var oldBad = Score(formula, new Post(Model: model[1], AgeHours: 24));
        var newGood = Score(formula, new Post(Model: model[2], AgeHours: 0));

        Assert.True(newBad > oldBad);
        Assert.True(newGood > newBad);
    }

    [Fact]
    public void The_shifted_model_is_never_negative_and_keeps_the_order()
    {
        var values = new[] { -1.5, -0.2, 0, 0.4, 2.3, 6.5 };
        var shifted = ProfileRules.NonNegativeModel(values, new Goals());
        Assert.All(shifted, v => Assert.True(v >= 0));
        Assert.Equal(values.OrderBy(v => v).Select(v => Array.IndexOf(values, v)), shifted.OrderBy(v => v).Select(v => Array.IndexOf(shifted, v)));
        // The lowest value the default goals allow (a certain skip, −1.5) becomes 0
        Assert.Equal(0, shifted[0], 6);
        Assert.Equal(1.5, shifted[2], 6);
    }

    [Fact]
    public void Popular_now_favours_likes_and_comments_counts_double_and_fades_over_6_hours()
    {
        var formula = Example("Popular now");
        Assert.True(Score(formula, new Post(Likes: 30)) > Score(formula, new Post(Likes: 3)));
        Assert.Equal(Score(formula, new Post(Likes: 10)), Score(formula, new Post(Comments: 5)), 6);
        Assert.Equal(0.5, Score(formula, new Post(Likes: 10, AgeHours: 7)) / Score(formula, new Post(Likes: 10, AgeHours: 1)), 6);
        // Ignores the built-in formula entirely
        Assert.Equal(Score(formula, new Post(Formula: 1, Likes: 5)), Score(formula, new Post(Formula: 9, Likes: 5)), 6);
    }

    [Fact]
    public void Conversations_lifts_posts_with_comments_and_leaves_the_rest_as_the_formula_has_them()
    {
        var formula = Example("Conversations");
        Assert.Equal(2, Score(formula, new Post(Formula: 2, Comments: 0)), 6);
        Assert.True(Score(formula, new Post(Comments: 10)) > Score(formula, new Post(Comments: 2)));
        // A post with a good conversation can overtake one the formula liked a bit more
        Assert.True(Score(formula, new Post(Formula: 2, Comments: 8)) > Score(formula, new Post(Formula: 3, Comments: 0)));
    }

    [Fact]
    public void Your_topics_only_keeps_a_fifth_of_the_score_for_posts_off_your_topics()
    {
        var formula = Example("Your topics only");
        Assert.Equal(2, Score(formula, new Post(Formula: 2, TopicMatch: 0.5)), 6);
        Assert.Equal(0.4, Score(formula, new Post(Formula: 2, TopicMatch: 0.1)), 6);
        Assert.True(Score(formula, new Post(Formula: 1, TopicMatch: 0.3)) > Score(formula, new Post(Formula: 4, TopicMatch: 0)));
    }

    [Theory]
    [InlineData("Trending")]
    [InlineData("fresh preset")]
    public void Trending_and_fresh_favour_posts_picking_up_speed_and_halve_every_3_hours(string which)
    {
        var formula = which == "Trending" ? Example("Trending") : FreshPreset;
        Assert.True(Score(formula, new Post(Velocity: 20)) > Score(formula, new Post(Velocity: 2)));
        Assert.Equal(0.5, Score(formula, new Post(AgeHours: 4)) / Score(formula, new Post(AgeHours: 1)), 6);
        Assert.Equal(Score(formula, new Post(Formula: 2)), Score(formula, new Post(Formula: 2, Velocity: 0)), 6);
    }

    [Fact]
    public void Every_example_and_preset_formula_gives_a_finite_score_of_zero_or_more_on_any_post()
    {
        var formulas = ScoreExpression.Examples.Select(e => e.Text).Append(FreshPreset).ToList();
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var post = new Post(
                Formula: random.NextDouble() * 50, Model: random.NextDouble() * 8, AgeHours: random.NextDouble() * 2000,
                Likes: random.Next(0, 5000), Comments: random.Next(0, 2000), TopicMatch: random.NextDouble(), Velocity: random.Next(0, 500));
            foreach (var formula in formulas)
            {
                var score = Score(formula, post);
                Assert.True(double.IsFinite(score) && score >= 0, $"{formula} gave {score} for {post}");
            }
        }
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(-2.0)]
    [InlineData(0.0)]
    public void A_boost_above_one_lifts_and_below_one_lowers_whatever_the_sign_of_the_score(double score)
    {
        Assert.True(ProfileRules.Multiply(score, 1.5) >= score);
        Assert.True(ProfileRules.Multiply(score, 0.5) <= score);
        Assert.Equal(score, ProfileRules.Multiply(score, 1));
        // 0 sinks it below every post that wasn't sunk, even the lowest
        Assert.True(ProfileRules.Multiply(score, 0) < -1000);
    }

    [Fact]
    public void Boosts_match_tags_authors_photos_and_follows()
    {
        var row = new double[RankingFeatures.Names.Length];
        row[RankingFeatures.Index("has_media")] = 1;
        row[RankingFeatures.Index("follows_author")] = 1;
        var post = Response("runs_14", "running");

        Assert.True(ProfileRules.Matches(new BoostRule { Match = BoostMatch.Tag, Value = "running" }, post, row));
        Assert.False(ProfileRules.Matches(new BoostRule { Match = BoostMatch.Tag, Value = "yoga" }, post, row));
        Assert.True(ProfileRules.Matches(new BoostRule { Match = BoostMatch.Author, Value = "runs_14" }, post, row));
        Assert.True(ProfileRules.Matches(new BoostRule { Match = BoostMatch.HasMedia }, post, row));
        Assert.True(ProfileRules.Matches(new BoostRule { Match = BoostMatch.Following }, post, row));
        Assert.False(ProfileRules.Matches(new BoostRule { Match = BoostMatch.NotFollowing }, post, row));
    }

    [Fact]
    public void Filters_leave_out_posts_by_tag_photos_and_likes()
    {
        var row = new double[RankingFeatures.Names.Length];
        row[RankingFeatures.Index("likes")] = 3;
        var post = Response("walks_2", "walking");

        Assert.True(ProfileRules.Passes(post, row, new FilterSettings()));
        Assert.False(ProfileRules.Passes(post, row, new FilterSettings { ExcludeTags = ["walking"] }));
        Assert.True(ProfileRules.Passes(post, row, new FilterSettings { OnlyTags = ["walking", "running"] }));
        Assert.False(ProfileRules.Passes(post, row, new FilterSettings { OnlyTags = ["running"] }));
        Assert.False(ProfileRules.Passes(post, row, new FilterSettings { MediaOnly = true }));
        Assert.True(ProfileRules.Passes(post, row, new FilterSettings { MinLikes = 3 }));
        Assert.False(ProfileRules.Passes(post, row, new FilterSettings { MinLikes = 4 }));
    }

    [Fact]
    public void Exploring_slots_keep_to_the_author_limit()
    {
        // Every post below the top 30 is by one author, who also has a post at the top: exploring must not repeat them
        var prolific = Guid.NewGuid();
        var candidates = Enumerable.Range(0, 30).Select(i => Candidate(i, i == 0 ? prolific : Guid.NewGuid()))
            .Concat(Enumerable.Range(30, 30).Select(i => Candidate(i, i % 2 == 0 ? prolific : Guid.NewGuid())))
            .ToList();
        var authorOf = candidates.ToDictionary(c => c.Post.PostId, c => c.AuthorId);

        var feed = DiversityReranker.Arrange(candidates, new Random(3), new DiversityRules(AuthorCap: 1, OutOfNetwork: 0));

        // Checked while other authors are left; once only the prolific author remains, the rule gives way by design
        for (var start = 0; start + 10 <= 30; start++)
        {
            Assert.True(feed.Skip(start).Take(10).Count(p => authorOf[p.PostId] == prolific) <= 1, $"window at {start}");
        }
        Assert.Contains(feed.Take(10), p => p.Explored);
    }

    private static DiversityReranker.Candidate Candidate(int rank, Guid author) =>
        new(new RankedPost(Guid.NewGuid(), 1000 - rank, new double[RankingFeatures.Names.Length], []), author, $"t{rank % 5}", true);

    private static HomeFeedPostResponse Response(string username, string tag) => new()
    {
        Id = Guid.NewGuid(),
        AuthorId = Guid.NewGuid(),
        AuthorExternalUserId = username,
        AuthorUsername = username,
        Content = "",
        Tags = [new TagResponse { Tag = tag, Source = "inferred" }],
    };
}
