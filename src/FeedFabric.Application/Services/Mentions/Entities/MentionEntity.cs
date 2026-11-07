using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Mentions;

// AuthorId mentioned MentionedUserId with an @username, in a post or, when CommentId is set, in a comment on it
public class MentionEntity : EntityBase
{
    public required Guid MentionedUserId { get; init; }

    public required Guid AuthorId { get; init; }

    public required Guid PostId { get; init; }

    public Guid? CommentId { get; init; }

    public DateTime CreatedAt { get; init; }
}
