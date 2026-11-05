using FeedFabric.Application.Services;
using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Application.Services.Comments;

// A comment on a post, or a reply to another comment on the same post when ParentCommentId is set
public class CommentEntity : EntityBase
{
    public required Guid PostId { get; set; }

    public required Guid AuthorId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; }

    // Soft delete: a deleted comment with replies stays as a placeholder so the thread under it still reads
    public DateTime? DeletedAt { get; set; }

    // Held for review, or removed by a reviewer: see ContentStatus
    public ContentStatus Status { get; set; }
}
