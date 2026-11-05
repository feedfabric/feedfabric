using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("follows")]
public class FollowsController(IFollowService followService, ICurrentUser currentUser) : ControllerBase
{
    // PUT /follows/{externalUserId}; repeating it is a no-op
    [HttpPut("{externalUserId}")]
    public async Task<IActionResult> Follow(string externalUserId, CancellationToken cancellationToken)
    {
        var status = await followService.FollowAsync(currentUser.ExternalUserId, externalUserId, cancellationToken);
        return status == FollowResultStatus.Ok ? NoContent() : ToProblem(status, externalUserId);
    }

    // DELETE /follows/{externalUserId}; repeating it is a no-op
    [HttpDelete("{externalUserId}")]
    public async Task<IActionResult> Unfollow(string externalUserId, CancellationToken cancellationToken)
    {
        var status = await followService.UnfollowAsync(currentUser.ExternalUserId, externalUserId, cancellationToken);
        return status == FollowResultStatus.Ok ? NoContent() : ToProblem(status, externalUserId);
    }

    private ObjectResult ToProblem(FollowResultStatus status, string targetExternalUserId) => status switch
    {
        FollowResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        FollowResultStatus.TargetNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound,
            $"User {targetExternalUserId} not found.", new { externalUserId = targetExternalUserId }),
        FollowResultStatus.CannotFollowSelf => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.FollowSelf,
            "Users can't follow themselves."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
