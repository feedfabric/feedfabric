namespace FeedFabric.Application.Services.Comments;

public class CommentResponse
{
    public required Guid Id { get; set; }

    public required Guid PostId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public required Guid AuthorId { get; set; }

    public required string AuthorExternalUserId { get; set; }

    public required string AuthorUsername { get; set; }

    // Null when the comment is deleted
    public string? Content { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    // Direct replies only; load them with GET /userfeed/comments/{id}/replies
    public int ReplyCount { get; set; }
}

public class CommentPageResponse
{
    public required IReadOnlyList<CommentResponse> Comments { get; set; }

    // Pass back as ?cursor= for the next page; null when there are no more comments
    public Guid? NextCursor { get; set; }
}
