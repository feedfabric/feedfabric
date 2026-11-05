using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;

namespace FeedFabric.Application.Tests.Ranking;

public class ScoreExpressionTests
{
    private static double Eval(string text, params (string Name, double Value)[] variables)
    {
        Assert.True(ScoreExpression.TryParse(text, out var expression, out var error), error?.Message);
        var values = new double[ScoreExpression.Width];
        foreach (var (name, value) in variables)
        {
            values[ScoreExpression.IndexOf(name)] = value;
        }
        return expression!.Evaluate(values);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("1 / 0", 0)]
    [InlineData("3 > 2 and 2 >= 2", 1)]
    [InlineData("not (1 == 1) or 0", 0)]
    [InlineData("if(1 < 2, 10, 20)", 10)]
    [InlineData("clamp(15, 0, 10)", 10)]
    [InlineData("max(min(3, 5), 4)", 4)]
    [InlineData("ln(0)", 0)]
    public void Arithmetic_comparisons_and_functions(string text, double expected) =>
        Assert.Equal(expected, Eval(text), 9);

    [Fact]
    public void Decays_start_at_one_and_fall_with_time()
    {
        Assert.Equal(1, Eval("decay_exp(0, 12)"));
        Assert.Equal(0.5, Eval("decay_exp(12, 12)"), 9);
        Assert.Equal(0.5, Eval("decay_linear(5, 10)"), 9);
        Assert.Equal(0, Eval("decay_linear(20, 10)"));
        Assert.Equal(0.5, Eval("decay_gauss(6, 6)"), 9);
    }

    [Fact]
    public void Reads_ranking_signals_and_the_extra_variables()
    {
        Assert.Equal(13.5, Eval("likes + 2 * comments + age_hours", ("likes", 5), ("comments", 4), ("age_hours", 0.5)), 9);
        Assert.Equal(6, Eval("formula * model", ("formula", 2), ("model", 3)), 9);
    }

    [Fact]
    public void Knows_when_it_needs_the_model()
    {
        ScoreExpression.TryParse("model * decay_exp(age_hours, 6)", out var withModel, out _);
        ScoreExpression.TryParse("formula * 2", out var without, out _);
        Assert.True(withModel!.UsesModel);
        Assert.False(without!.UsesModel);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("likes +", "ends too soon")]
    [InlineData("likes * bananas", "no variable called 'bananas'")]
    [InlineData("position * 2", "no variable called 'position'")]
    [InlineData("rm(1)", "no function called 'rm'")]
    [InlineData("min(1)", "takes 2 values")]
    [InlineData("(1 + 2", "Expected ')'")]
    [InlineData("1 $ 2", "Unexpected '$'")]
    public void Explains_what_is_wrong(string text, string message)
    {
        Assert.False(ScoreExpression.TryParse(text, out _, out var error));
        Assert.Contains(message, error!.Message);
    }

    [Fact]
    public void Points_at_the_problem()
    {
        ScoreExpression.TryParse("likes * bananas", out _, out var error);
        Assert.Equal(8, error!.Position);
    }

    [Fact]
    public void Never_returns_infinity_or_nan()
    {
        Assert.Equal(0, Eval("exp(1000) * 10 ^ 400"));
        Assert.Equal(0, Eval("(0 - 1) ^ 0.5"));
    }

    [Fact]
    public void Refuses_very_deep_nesting()
    {
        var deep = new string('(', 100) + "1" + new string(')', 100);
        Assert.False(ScoreExpression.TryParse(deep, out _, out var error));
        Assert.Contains("nested too deeply", error!.Message);
    }

    [Fact]
    public void Every_preset_is_valid()
    {
        foreach (var (key, preset) in RankingPresets.All)
        {
            Assert.True(preset().Validate().Count == 0, $"{key}: {string.Join(", ", preset().Validate().Select(e => $"{e.Field} {e.Message}"))}");
        }
    }

    [Fact]
    public void Validation_names_each_setting_that_is_out_of_range()
    {
        var profile = new RankingProfile
        {
            Name = "Too much",
            Variety = new VarietySettings { AuthorCap = 0, SeenPenalty = 2 },
            Exploration = new ExplorationSettings { Slots = 5 },
            Scoring = new ScoringSettings { Mode = ScoringMode.Expression, Expression = "likes *" },
            Boosts = [new BoostRule { Match = BoostMatch.Tag, Multiplier = 2 }],
        };

        var fields = profile.Validate().Select(e => e.Field).ToList();

        Assert.Equal(["scoring.expression", "variety.authorCap", "variety.seenPenalty", "exploration.slots", "boosts[0].value"], fields);
    }

    [Fact]
    public void Tags_and_usernames_are_normalised()
    {
        var profile = new RankingProfile
        {
            Boosts = [new BoostRule { Match = BoostMatch.Author, Value = " @Sim_Runner " }],
            Filters = new FilterSettings { ExcludeTags = ["#Politics", "politics", " "] },
        }.Normalised();

        Assert.Equal("sim_runner", profile.Boosts[0].Value);
        Assert.Equal(["politics"], profile.Filters.ExcludeTags);
    }
}
