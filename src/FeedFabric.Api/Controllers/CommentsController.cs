using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FeedFabric.Api.Errors;
using FeedFabric.Api.RateLimiting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed")]
public class CommentsController(ICommentService commentService, ICurrentUser currentUser) : ControllerBase
{
    // GET /userfeed/posts/{postId}/comments?limit=20, then add &cursor=<nextCursor>; replies load separately
    [HttpGet("posts/{postId:guid}/comments")]
    public async Task<ActionResult<CommentPageResponse>> List(Guid postId, [FromQuery] CommentQuery query, CancellationToken cancellationToken)
    {
        var result = await commentService.ListAsync(currentUser.ExternalUserId, postId, query, cancellationToken);
        return result.Status == CommentResultStatus.Ok ? Ok(result.Page) : ToProblem(result.Status);
    }

    // GET /userfeed/comments/{commentId}/replies?limit=20, then add &cursor=<nextCursor>
    [HttpGet("comments/{commentId:guid}/replies")]
    public async Task<ActionResult<CommentPageResponse>> ListReplies(Guid commentId, [FromQuery] CommentQuery query, CancellationToken cancellationToken)
    {
        var result = await commentService.ListRepliesAsync(currentUser.ExternalUserId, commentId, query, cancellationToken);
        return result.Status == CommentResultStatus.Ok ? Ok(result.Page) : ToProblem(result.Status);
    }

    // POST /userfeed/posts/{postId}/comments; set parentCommentId in the body to reply to a comment
    [EnableRateLimiting(RateLimitPolicies.Commenting)]
    [HttpPost("posts/{postId:guid}/comments")]
    public async Task<ActionResult<CommentResponse>> Create(Guid postId, CommentRequest request, CancellationToken cancellationToken)
    {
        var result = await commentService.CreateAsync(currentUser.ExternalUserId, postId, request, cancellationToken);
        return result.Status == CommentResultStatus.Ok
            ? StatusCode(StatusCodes.Status201Created, result.Comment)
            : ToProblem(result.Status, result.Violation);
    }

    [EnableRateLimiting(RateLimitPolicies.Commenting)]
    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid commentId, CancellationToken cancellationToken)
    {
        var status = await commentService.DeleteAsync(currentUser.ExternalUserId, commentId, cancellationToken);
        return status == CommentResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    private ObjectResult ToProblem(CommentResultStatus status, string? violation = null) => status switch
    {
        CommentResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        CommentResultStatus.PostNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound,
            "Post not found."),
        CommentResultStatus.CommentNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.CommentNotFound,
            "Comment not found."),
        CommentResultStatus.ContentViolation => this.ApiProblem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.ContentViolation,
            "This comment breaks the community rules.", new { category = violation }),
        CommentResultStatus.Forbidden => this.ApiProblem(StatusCodes.Status403Forbidden, ErrorCodes.CommentNotAuthor,
            "Only the author can delete this comment."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
