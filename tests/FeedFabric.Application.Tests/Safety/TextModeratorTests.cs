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
    [InlineData("Sex after pregnancy hurt for months, nobody warns you. Talk to your GP.")]
    public async Task The_classifier_lets_everyday_posts_and_mild_swearing_through(string text)
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        Assert.Null(await Create().CheckAsync(text, TestContext.Current.CancellationToken));
    }

    // The pretrained model flags these; fine-tuning on this app's wording is what lets them through
    [Theory]
    [InlineData("Kill it at the race tomorrow everyone!")]
    [InlineData("This hill is a bitch but I love it")]
    public async Task The_tuned_classifier_lets_sporting_talk_through(string text)
    {
        Models.SkipUnlessTuned("models/safety/toxicity");

        Assert.Null(await Create().CheckAsync(text, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("You're an idiot")]
    [InlineData("shut up you stupid moron")]
    [InlineData("y0u are a w0rthless 1diot")]
    [InlineData("ur so dumb lol")]
    [InlineData("What an idiot you are")]
    [InlineData("Get lost, yourself and your stupid club")]
    public void Second_person_text_addresses_someone(string text)
    {
        Assert.True(TextModerator.AddressesSomeone(text));
    }

    [Theory]
    [InlineData("Physio signed me off today after 12 weeks! Grinning like an idiot.")]
    [InlineData("Felt like such an idiot when I forgot my trainers")]
    [InlineData("Went to my first ever gym class today! Grinning like an idiot. with @beginner_816")]
    [InlineData("Youth league starts Sunday, bring your boots")]
    [InlineData("Our tour of Uruguay was brilliant")]
    public void Text_about_yourself_or_others_does_not(string text)
    {
        // "your" in the fourth one does address someone; the rest don't
        Assert.Equal(text.Contains("your"), TextModerator.AddressesSomeone(text));
    }

    // Insults only count when aimed at someone: laughing at yourself passes, the same word at someone is flagged
    [Fact]
    public async Task Insults_are_flagged_only_when_aimed_at_someone()
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        Assert.Null(await Create().CheckAsync("Physio signed me off today after 12 weeks! Grinning like an idiot.", TestContext.Current.CancellationToken));
        var aimed = await Create().CheckAsync("You're an idiot and nobody wants you here", TestContext.Current.CancellationToken);
        Assert.Equal(ModerationAction.Flag, aimed?.Action);
    }

    [Theory]
    [InlineData("I know where you live and I am going to kill you")]
    [InlineData("you are a worthless pathetic idiot and everyone hates you, just disappear")]
    [InlineData("y0u are a w0rthless 1diot")]
    public async Task The_classifier_stops_threats_and_severe_abuse(string text)
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        var verdict = await Create().CheckAsync(text, TestContext.Current.CancellationToken);

        Assert.NotNull(verdict);
        Assert.Contains(verdict.Category, new[] { ModerationCategories.Threat, ModerationCategories.SevereToxicity, ModerationCategories.Insult });
    }

    [Fact]
    public async Task Threats_are_blocked_and_insults_flagged_for_review()
    {
        Models.SkipUnlessPresent("models/safety/toxicity");

        var threat = await Create().CheckAsync("I know where you live and I am going to kill you", TestContext.Current.CancellationToken);
        var insult = await Create().CheckAsync("Shut up you stupid moron", TestContext.Current.CancellationToken);

        Assert.Equal(ModerationAction.Block, threat?.Action);
        Assert.Equal(ModerationAction.Flag, insult?.Action);
    }

    [Fact]
    public async Task Rule_breaks_are_blocked()
    {
        var verdict = await Create().CheckAsync("This contains BadWord in it", TestContext.Current.CancellationToken);

        Assert.Equal(new ModerationVerdict(ModerationAction.Block, ModerationCategories.BlockedTerm, 1), verdict);
    }
}
