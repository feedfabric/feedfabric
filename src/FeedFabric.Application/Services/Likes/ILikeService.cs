namespace FeedFabric.Application.Services.Likes;

public interface ILikeService
{
    // Liking a post you already like succeeds and changes nothing
    Task<LikeResultStatus> LikeAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);

    // Unliking a post you don't like succeeds and changes nothing
    Task<LikeResultStatus> UnlikeAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);
}
