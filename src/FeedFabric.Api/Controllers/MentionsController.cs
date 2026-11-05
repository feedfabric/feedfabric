using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed/mentions")]
public class MentionsController(IMentionService mentionService, ICurrentUser currentUser) : ControllerBase
{
    // GET /userfeed/mentions?limit=20, then add &cursor=<nextCursor>: posts and comments that @mention you, newest first
    [HttpGet]
    public async Task<ActionResult<MentionPageResponse>> List([FromQuery] HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var page = await mentionService.ListAsync(currentUser.ExternalUserId, query, cancellationToken);
        return page is null
            ? this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
                $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId })
            : Ok(page);
    }
}
