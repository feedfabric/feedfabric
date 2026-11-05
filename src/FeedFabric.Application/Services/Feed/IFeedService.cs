namespace FeedFabric.Application.Services.Feed;

public interface IFeedService
{
    // The user's own posts filtered by FeedQuery; null if the user hasn't been synced
    Task<FeedResponse?> GetFeedAsync(string externalUserId, FeedQuery query, CancellationToken cancellationToken);
}
