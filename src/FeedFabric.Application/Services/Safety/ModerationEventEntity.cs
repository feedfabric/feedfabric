using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Safety;

// Something that was stopped before anyone saw it: a post, comment or photo that broke a rule. Kept so moderators
// and the dashboard can see what is being caught, and so repeat offenders can be found.
public class ModerationEventEntity : EntityBase
{
    public required Guid UserId { get; set; }

    // "post", "comment" or "photo"
    public required string Kind { get; set; }

    // ModerationCategories
    public required string Category { get; set; }

    // The classifier's score for photos; 1 for rule matches
    public double Score { get; set; }

    // The start of the text that was stopped (never the photo itself, which is deleted)
    public string? Excerpt { get; set; }

    public DateTime CreatedAt { get; set; }
}

public static class ModerationCategories
{
    public const string Nudity = "nudity";
    public const string BlockedTerm = "blocked_term";
    public const string LinkSpam = "link_spam";
    public const string Flooding = "flooding";

    // From the offensive-language classifier
    public const string SevereToxicity = "severe_toxicity";
    public const string Threat = "threat";
    public const string Hate = "hate";
    public const string Insult = "insult";
    public const string Obscene = "obscene";

    public static string ForToxicityLabel(string label) => label switch
    {
        "severe_toxic" => SevereToxicity,
        "threat" => Threat,
        "identity_hate" => Hate,
        "insult" => Insult,
        "obscene" => Obscene,
        _ => label,
    };
}
