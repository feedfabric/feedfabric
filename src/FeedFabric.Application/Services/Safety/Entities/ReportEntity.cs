using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Safety;

// ReporterId reported a post, or a comment on it when CommentId is set. One report per person per thing.
public class ReportEntity : EntityBase
{
    public required Guid ReporterId { get; init; }

    public required Guid PostId { get; init; }

    public Guid? CommentId { get; init; }

    // ReportReasons
    public required string Reason { get; init; }

    public DateTime CreatedAt { get; init; }
}

public static class ReportReasons
{
    public const string Spam = "spam";
    public const string Harassment = "harassment";
    public const string Hate = "hate";
    public const string Threat = "threat";
    public const string Sexual = "sexual";
    public const string SelfHarm = "self_harm";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Spam, Harassment, Hate, Threat, Sexual, SelfHarm, Other];
}
