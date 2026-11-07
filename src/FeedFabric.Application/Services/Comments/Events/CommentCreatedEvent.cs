namespace FeedFabric.Application.Services.Comments;

// Data for EventTypes.CommentCreated and EventTypes.CommentDeleted. The commenter is the envelope's ActorId.
public class CommentCreatedEvent
{
    public required Guid CommentId { get; init; }

    public required Guid PostId { get; init; }

    public required Guid PostAuthorId { get; init; }

    public Guid? ParentCommentId { get; set; }
}
