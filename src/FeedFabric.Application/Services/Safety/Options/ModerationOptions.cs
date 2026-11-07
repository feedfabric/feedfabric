namespace FeedFabric.Application.Services.Safety;

public class ModerationOptions
{
    public const string Section = "Moderation";

    // An image the nudity classifier scores at or above this is rejected (0 to 1)
    public double NudityRejectAt { get; set; } = 0.8;

    // Words and phrases a post or comment can't contain, matched whole and ignoring case. Keep the real list in
    // configuration (or a secret store), not in the code.
    public List<string> BlockedTerms { get; init; } = [];

    // More links than this in one post or comment is treated as spam
    public int MaxLinks { get; set; } = 2;

    // The same character repeated this many times or more is treated as flooding ("!!!!!!!!!!!!")
    public int MaxRepeatedCharacters { get; set; } = 12;

    // The offensive-language classifier's labels and the score (0 to 1) at which each acts on a post or comment.
    // Threats are refused outright, since publishing one is the harm. Severe abuse and hate are held early, sexually
    // explicit messages only when the model is sure: held means saved but seen only by the author until a reviewer
    // decides. Insults are flagged instead (ToxicityFlagAt): published, and put in the review queue, since a wrongly
    // hidden post costs more than an insult a moderator removes soon after; and only when aimed at someone
    // (ToxicityOnlyWhenAddressed). Ordinary swearing ("bloody knackered after that run") isn't stopped, so obscene isn't
    // listed; labels in neither list never act. ml/tune_toxicity.py tests the model against both, so keep its
    // LIMITS in step.
    public Dictionary<string, double> ToxicityRejectAt { get; set; } = new()
    {
        ["threat"] = 0.6,
    };

    public Dictionary<string, double> ToxicityHoldAt { get; set; } = new()
    {
        ["severe_toxicity"] = 0.5,
        ["identity_attack"] = 0.6,
        ["sexual_explicit"] = 0.9,
    };

    public Dictionary<string, double> ToxicityFlagAt { get; set; } = new()
    {
        ["insult"] = 0.9,
    };

    // Labels that only act when the text speaks to someone ("you", "your", "u"...). The classifier scores "grinning like
    // an idiot" as an insult as surely as "you stupid idiot", so without this everyday posts that laugh at the writer
    // are held. Insults about someone in the third person ("Tom is an idiot") aren't held either; reports catch those.
    public List<string> ToxicityOnlyWhenAddressed { get; set; } = ["insult"];

    // A post or comment this many different people have reported is held for review
    public int ReportsToHold { get; set; } = 3;
}
