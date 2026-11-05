using FeedFabric.Application.Services.Feed;

namespace FeedFabric.Application.Services.Mentions;

public interface IMentionService
{
    // Newest first; null if the user hasn't been synced. Mentions in deleted posts or comments are left out.
    Task<MentionPageResponse?> ListAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken);
}
