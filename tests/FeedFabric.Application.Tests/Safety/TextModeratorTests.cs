using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Application.Tests.Safety;

public class TextModeratorTests
{
    private static TextModerator Create(ModerationOptions? options = null) =>
        new(Options.Create(options ?? new ModerationOptions { BlockedTerms = ["badword", "buy followers"] }), new ToxicityClassifier(Models.Storage()));

    [Theory]
    [InlineData("This contains BadWord in it", ModerationCategories.BlockedTerm)]
    [InlineData("you can buy followers here", ModerationCategories.BlockedTerm)]
    [InlineData("https://a.example https://b.example https://c.example", ModerationCategories.LinkSpam)]
    [InlineData("Amazing!!!!!!!!!!!!!!!!", ModerationCategories.Flooding)]
    public void Rules_catch_blocked_terms_link_spam_and_flooding(string text, string category)
    {
        Assert.Equal(category, Create().CheckRules(text));
    }

    [Theory]
    [InlineData("A badwordish thing")]          // a blocked term inside another word isn't a match
    [InlineData("Read this: https://a.example")]
    [InlineData("First 10k done!!!")]
    public void Rules_let_ordinary_posts_through(string text)
    {
        Assert.Null(Create().CheckRules(text));
    }

    [Theory]
    [InlineData("First gentle walk since the injury, 40 minutes, legs like jelly. Proud of myself.")]
    [InlineData("Bloody knackered after that run, damn hills. Worth it though.")]
    [InlineData("Day 21 of no screens after 9 and I sleep so much better")]
    public async Task The_classifier_lets_everyday_posts_and_mild_swearing_through(string text)
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        Assert.Null(await Create().CheckAsync(text, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("I know where you live and I am going to kill you")]
    [InlineData("you are a worthless pathetic idiot and everyone hates you, just disappear")]
    public async Task The_classifier_stops_threats_and_severe_abuse(string text)
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        var violation = await Create().CheckAsync(text, TestContext.Current.CancellationToken);

        Assert.NotNull(violation);
        Assert.Contains(violation.Value.Category, new[] { ModerationCategories.Threat, ModerationCategories.SevereToxicity, ModerationCategories.Insult });
    }
}
