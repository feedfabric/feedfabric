namespace FeedFabric.Application.Services.Likes;

// UserId likes PostId; the pair is the key, so liking twice is a no-op
public class LikeEntity
{
    public required Guid UserId { get; set; }

    public required Guid PostId { get; set; }

    public DateTime CreatedAt { get; set; }
}
