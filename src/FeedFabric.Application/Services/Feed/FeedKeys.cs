namespace FeedFabric.Application.Services.Feed;

// Home timelines live in Redis as one sorted set per user: member = post ID, score = CreatedAt in Unix milliseconds
public static class FeedKeys
{
    public static string ForUser(Guid userId) => $"feed:{userId}";
}
