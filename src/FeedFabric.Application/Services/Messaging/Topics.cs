namespace FeedFabric.Application.Services.Messaging;

// One Kafka topic per owning service
public static class Topics
{
    public const string Posts = "feedfabric.posts";

    public const string Engagement = "feedfabric.engagement";

    // Scanning and image processing are slow, so they get their own topic and never hold up feeds or engagement
    public const string Media = "feedfabric.media";

    public static string ForEventType(string type) => type switch
    {
        EventTypes.PostCreated or EventTypes.PostUpdated => Posts,
        EventTypes.PostLiked or EventTypes.PostUnliked or EventTypes.CommentCreated or EventTypes.CommentDeleted
            or EventTypes.UserMentioned => Engagement,
        EventTypes.MediaUploaded => Media,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No topic for this event type."),
    };
}
