namespace FeedFabric.Application.Services.Reposts;

public interface IRepostService
{
    // Reposting a post you already reposted succeeds and changes nothing. Reposting a repost reposts its original.
    Task<RepostResultStatus> RepostAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);

    // Undoing a repost you haven't made succeeds and changes nothing
    Task<RepostResultStatus> UnrepostAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);
}
