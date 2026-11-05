namespace FeedFabric.Application.Services.Safety;

public class ModerationOptions
{
    public const string Section = "Moderation";

    // An image the nudity classifier scores at or above this is rejected (0 to 1)
    public double NudityRejectAt { get; set; } = 0.8;

    // Words and phrases a post or comment can't contain, matched whole and ignoring case. Keep the real list in
    // configuration (or a secret store), not in the code.
    public List<string> BlockedTerms { get; set; } = [];

    // More links than this in one post or comment is treated as spam
    public int MaxLinks { get; set; } = 2;

    // The same character repeated this many times or more is treated as flooding ("!!!!!!!!!!!!")
    public int MaxRepeatedCharacters { get; set; } = 12;

    // The offensive-language classifier's labels and the score (0 to 1) at which each acts on a post or comment.
    // Threats are refused outright, since publishing one is the harm. Severe abuse and hate are held early, insults
    // and sexually explicit messages only when the model is sure: held means saved but seen only by the author until
    // a reviewer decides. Ordinary swearing ("bloody knackered after that run") isn't stopped, so obscene isn't
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
        ["insult"] = 0.9,
        ["sexual_explicit"] = 0.9,
    };

    // A post or comment this many different people have reported is held for review
    public int ReportsToHold { get; set; } = 3;
}
