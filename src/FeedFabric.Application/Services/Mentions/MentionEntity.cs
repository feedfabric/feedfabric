using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Mentions;

// AuthorId mentioned MentionedUserId with an @username, in a post or, when CommentId is set, in a comment on it
public class MentionEntity : EntityBase
{
    public required Guid MentionedUserId { get; set; }

    public required Guid AuthorId { get; set; }

    public required Guid PostId { get; set; }

    public Guid? CommentId { get; set; }

    public DateTime CreatedAt { get; set; }
}
