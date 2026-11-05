using FeedFabric.Application.Services.Feed;

namespace FeedFabric.Application.Services.Tags;

public interface ITagService
{
    // Newest posts with the tag, written or inferred
    Task<HomeFeedResponse> PostsAsync(string tag, HomeFeedQuery query, CancellationToken cancellationToken);

    // Tags on the most posts created in the last `hours`
    Task<IReadOnlyList<TrendingTagResponse>> TrendingAsync(int hours, int limit, CancellationToken cancellationToken);
}
