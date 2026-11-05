namespace FeedFabric.Application.Services.Messaging;

// Event type names, stored in OutboxMessageEntity.Type and the Kafka "type" header. Never rename one.
public static class EventTypes
{
    public const string PostCreated = "PostCreated";
    
    public const string PostDeleted = "PostDeleted";
    
    public const string PostUpdated = "PostUpdated";
    
    public const string PostHovered = "PostHovered";
    
    public const string PostClicked = "PostClicked";
    
    public const string PostLiked = "PostLiked";

    public const string PostUnliked = "PostUnliked";

    public const string CommentCreated = "CommentCreated";

    public const string CommentDeleted = "CommentDeleted";

    public const string UserMentioned = "UserMentioned";

    public const string MediaUploaded = "MediaUploaded";
}
