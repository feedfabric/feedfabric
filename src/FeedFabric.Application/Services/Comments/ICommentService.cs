namespace FeedFabric.Application.Services.Comments;

public interface ICommentService
{
    // Comments on the post itself, without their replies. Held comments only show to their author.
    Task<CommentResult> ListAsync(string externalUserId, Guid postId, CommentQuery query, CancellationToken cancellationToken);

    // Direct replies to one comment
    Task<CommentResult> ListRepliesAsync(string externalUserId, Guid commentId, CommentQuery query, CancellationToken cancellationToken);

    Task<CommentResult> CreateAsync(string externalUserId, Guid postId, CommentRequest request, CancellationToken cancellationToken);

    // Only the author can delete; deleting twice succeeds
    Task<CommentResultStatus> DeleteAsync(string externalUserId, Guid commentId, CancellationToken cancellationToken);
}
