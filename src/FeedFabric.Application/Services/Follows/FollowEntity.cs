namespace FeedFabric.Application.Services.Follows;

// FollowerId follows FolloweeId; the pair is the key, so following twice is a no-op
public class FollowEntity
{
    public required Guid FollowerId { get; set; }

    public required Guid FolloweeId { get; set; }

    public DateTime CreatedAt { get; set; }
}
