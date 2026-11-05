namespace FeedFabric.Application.Services.Posts;

public interface IPostService
{
    Task<PostResult> CreateAsync(string externalUserId, PostRequest request, CancellationToken cancellationToken);

    // Only the author can edit, and only within PostService.EditWindow of posting
    Task<PostResult> EditAsync(string externalUserId, Guid postId, PostRequest request, CancellationToken cancellationToken);

    // Only the author can delete
    Task<PostResult> DeleteAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);
}
