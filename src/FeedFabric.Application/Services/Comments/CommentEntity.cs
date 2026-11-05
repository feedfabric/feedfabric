using FeedFabric.Application.Services;

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
}
