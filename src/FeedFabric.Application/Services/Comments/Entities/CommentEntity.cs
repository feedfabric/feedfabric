using FeedFabric.Application.Services;
using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Application.Services.Comments;

// A comment on a post, or a reply to another comment on the same post when ParentCommentId is set
public class CommentEntity : EntityBase
{
    public required Guid PostId { get; init; }

    public required Guid AuthorId { get; init; }

    public Guid? ParentCommentId { get; init; }

    public required string Content { get; init; }

    public DateTime CreatedAt { get; init; }

    // Soft delete: a deleted comment with replies stays as a placeholder so the thread under it still reads
    public DateTime? DeletedAt { get; set; }

    // Held for review, or removed by a reviewer: see ContentStatus
    public ContentStatus Status { get; set; }
}
