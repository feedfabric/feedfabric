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

    // The offensive-language classifier's labels and the score (0 to 1) at which each stops a post or comment.
    // Aimed at extremely offensive language: hate, threats and severe abuse are stopped early; insults only when the
    // model is sure; ordinary swearing ("bloody knackered after that run") isn't stopped. Labels not listed never stop.
    public Dictionary<string, double> ToxicityRejectAt { get; set; } = new()
    {
        ["severe_toxic"] = 0.5,
        ["threat"] = 0.6,
        ["identity_hate"] = 0.6,
        ["insult"] = 0.9,
        ["obscene"] = 0.97,
    };
}
