using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Safety;

// Something moderation acted on: a post, comment or photo that was blocked, held for review, or reported enough to be
// held. Kept so the dashboard can show what is being caught, so repeat offenders can be found, and, once a reviewer
// has decided (Decision), as labelled examples for training the classifier.
public class ModerationEventEntity : EntityBase
{
    // Whose post, comment or photo it was
    public required Guid UserId { get; set; }

    // ModerationActions
    public string Action { get; set; } = ModerationActions.Blocked;

    // "post", "comment", "photo", or "user" for a suspension or reinstatement
    public required string Kind { get; set; }

    // ModerationCategories
    public required string Category { get; set; }

    // The classifier's score for photos; 1 for rule matches
    public double Score { get; set; }

    // The start of the text that was stopped (never the photo itself, which is deleted)
    public string? Excerpt { get; set; }

    // What was held or reported; blocked things were never saved, so they have neither
    public Guid? PostId { get; set; }

    public Guid? CommentId { get; set; }

    public DateTime CreatedAt { get; set; }

    // Set when a reviewer decides: ReviewDecisions, and who (their external user ID)
    public string? Decision { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public static string ExcerptOf(string text) => text.Length > 120 ? text[..117] + "…" : text;
}

public static class ModerationActions
{
    // Stopped when posted; nothing was saved
    public const string Blocked = "blocked";

    // Saved, but only the author sees it until a reviewer decides
    public const string Held = "held";

    // Enough people reported it that it was held
    public const string Reported = "reported";

    // A reviewer removed it without it having been flagged
    public const string Reviewed = "reviewed";
}

public static class ReviewDecisions
{
    public const string Approved = "approved";
    public const string Removed = "removed";

    // On a Kind "user" event: the account's new status
    public const string Suspended = "suspended";
    public const string Reinstated = "reinstated";
}

public static class ModerationCategories
{
    public const string Nudity = "nudity";
    public const string BlockedTerm = "blocked_term";
    public const string LinkSpam = "link_spam";
    public const string Flooding = "flooding";

    // Held because enough people reported it
    public const string Reported = "reported";

    // Removed by a reviewer who found it themselves
    public const string Manual = "manual";

    // Staff suspended or reinstated the account (Kind "user")
    public const string Suspended = "suspended";
    public const string Reinstated = "reinstated";

    // From the offensive-language classifier
    public const string SevereToxicity = "severe_toxicity";
    public const string Threat = "threat";
    public const string Hate = "hate";
    public const string Insult = "insult";
    public const string Obscene = "obscene";
    public const string SexuallyExplicit = "sexual_explicit";

    public static string ForToxicityLabel(string label) => label switch
    {
        "severe_toxicity" => SevereToxicity,
        "threat" => Threat,
        "identity_attack" => Hate,
        "insult" => Insult,
        "obscene" => Obscene,
        "sexual_explicit" => SexuallyExplicit,
        _ => label,
    };
}
