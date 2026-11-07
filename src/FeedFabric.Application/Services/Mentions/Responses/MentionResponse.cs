namespace FeedFabric.Application.Services.Mentions;

public class MentionResponse
{
    public required Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public required string AuthorExternalUserId { get; set; }

    public required string AuthorUsername { get; set; }

    public required Guid PostId { get; set; }

    // Set when the mention is in a comment rather than the post itself
    public Guid? CommentId { get; set; }

    // The text the mention is in
    public required string Content { get; set; }
}

public class MentionPageResponse
{
    public required IReadOnlyList<MentionResponse> Mentions { get; set; }

    // Pass back as ?cursor= for the next, older page; null when there are no more
    public Guid? NextCursor { get; set; }
}
