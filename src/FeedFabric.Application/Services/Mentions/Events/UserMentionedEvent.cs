namespace FeedFabric.Application.Services.Mentions;

// Data for EventTypes.UserMentioned. Whoever wrote the mention is the envelope's ActorId.
public class UserMentionedEvent
{
    public required Guid MentionedUserId { get; set; }

    public required Guid PostId { get; set; }

    public Guid? CommentId { get; set; }
}
