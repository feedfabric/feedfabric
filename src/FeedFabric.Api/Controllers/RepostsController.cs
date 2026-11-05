using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Reposts;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed/posts/{postId:guid}/repost")]
public class RepostsController(IRepostService repostService, ICurrentUser currentUser) : ControllerBase
{
    // PUT /userfeed/posts/{postId}/repost; repeating it is a no-op
    [HttpPut]
    public async Task<IActionResult> Repost(Guid postId, CancellationToken cancellationToken)
    {
        var status = await repostService.RepostAsync(currentUser.ExternalUserId, postId, cancellationToken);
        return status == RepostResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    // DELETE /userfeed/posts/{postId}/repost; repeating it is a no-op
    [HttpDelete]
    public async Task<IActionResult> Unrepost(Guid postId, CancellationToken cancellationToken)
    {
        var status = await repostService.UnrepostAsync(currentUser.ExternalUserId, postId, cancellationToken);
        return status == RepostResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    private ObjectResult ToProblem(RepostResultStatus status) => status switch
    {
        RepostResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        RepostResultStatus.PostNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound,
            "Post not found."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
