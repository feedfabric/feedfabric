namespace FeedFabric.Application.Services.Feed;

public interface IHomeFeedService
{
    // Posts from the accounts the user follows, plus their own; null if the user hasn't been synced
    Task<HomeFeedResponse?> GetHomeFeedAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken);
}
