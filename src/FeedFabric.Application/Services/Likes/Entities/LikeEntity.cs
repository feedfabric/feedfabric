namespace FeedFabric.Application.Services.Likes;

// UserId likes PostId; the pair is the key, so liking twice is a no-op
public class LikeEntity
{
    public required Guid UserId { get; init; }

    public required Guid PostId { get; init; }

    public DateTime CreatedAt { get; init; }
}
