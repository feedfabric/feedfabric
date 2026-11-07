namespace FeedFabric.Application.Services.Follows;

// FollowerId follows FolloweeId; the pair is the key, so following twice is a no-op
public class FollowEntity
{
    public required Guid FollowerId { get; init; }

    public required Guid FolloweeId { get; init; }

    public DateTime CreatedAt { get; init; }
}
