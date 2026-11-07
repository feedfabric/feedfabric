using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FeedFabric.Api.Errors;
using FeedFabric.Api.RateLimiting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.Reactions)]
[Authorize]
[Route("userfeed/posts/{postId:guid}/like")]
public class LikesController(ILikeService likeService, ICurrentUser currentUser) : ControllerBase
{
    // PUT /userfeed/posts/{postId}/like; repeating it is a no-op
    [HttpPut]
    public async Task<IActionResult> Like(Guid postId, CancellationToken cancellationToken)
    {
        var status = await likeService.LikeAsync(currentUser.ExternalUserId, postId, cancellationToken);
        return status == LikeResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    // DELETE /userfeed/posts/{postId}/like; repeating it is a no-op
    [HttpDelete]
    public async Task<IActionResult> Unlike(Guid postId, CancellationToken cancellationToken)
    {
        var status = await likeService.UnlikeAsync(currentUser.ExternalUserId, postId, cancellationToken);
        return status == LikeResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    private ObjectResult ToProblem(LikeResultStatus status) => status switch
    {
        LikeResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        LikeResultStatus.PostNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound,
            "Post not found."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
