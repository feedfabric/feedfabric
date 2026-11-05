namespace FeedFabric.Application.Services.Comments;

// Data for EventTypes.CommentCreated and EventTypes.CommentDeleted. The commenter is the envelope's ActorId.
public class CommentCreatedEvent
{
    public required Guid CommentId { get; set; }

    public required Guid PostId { get; set; }

    public required Guid PostAuthorId { get; set; }

    public Guid? ParentCommentId { get; set; }
}
