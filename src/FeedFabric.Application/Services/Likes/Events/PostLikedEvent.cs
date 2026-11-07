namespace FeedFabric.Application.Services.Likes;

// Data for EventTypes.PostLiked and EventTypes.PostUnliked. The liker is the envelope's ActorId.
public class PostLikedEvent
{
    public required Guid PostId { get; init; }

    public required Guid AuthorId { get; init; }
}
