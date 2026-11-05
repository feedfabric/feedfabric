namespace FeedFabric.Application.Services.Follows;

public interface IFollowService
{
    // Following someone you already follow succeeds and changes nothing
    Task<FollowResultStatus> FollowAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken);

    // Unfollowing someone you don't follow succeeds and changes nothing
    Task<FollowResultStatus> UnfollowAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken);
}
