using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// Someone's feeds as they'd see them, for staff. Nothing here is logged as served or touches the member's own For
// You session, so looking never becomes training data or changes what they see.
[ApiController]
[Authorize(Policy = AdminPolicies.Staff)]
[Route("admin/feed/{externalUserId}")]
public class AdminFeedController(IHomeFeedService homeFeed, ForYouService forYou, RankingProfileStore profiles,
    ICurrentUser currentUser) : ControllerBase
{
    // GET /admin/feed/{externalUserId}/following?limit=20, then add &cursor=<nextCursor>: newest first, from the
    // accounts they follow
    [HttpGet("following")]
    public async Task<ActionResult<HomeFeedResponse>> Following(string externalUserId, [FromQuery] HomeFeedQuery query,
        CancellationToken cancellationToken) =>
        await homeFeed.PeekAsync(externalUserId, currentUser.ExternalUserId, query, cancellationToken) is { } feed ? feed : NotFoundUser(externalUserId);

    // GET /admin/feed/{externalUserId}/foryou?limit=20&profile=<key>, then add &cursor=<nextCursor>: ranked for them,
    // each post with why it's there. The first page ranks again.
    [HttpGet("foryou")]
    public async Task<ActionResult<ForYouResponse>> ForYou(string externalUserId, [FromQuery] ForYouQuery query, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(query.Profile, cancellationToken);
        if (profile is null)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.ProfileNotFound, $"There's no ranking profile called '{query.Profile}'.",
                new { profile = query.Profile });
        }
        return await forYou.PeekAsync(externalUserId, currentUser.ExternalUserId, query, profile, cancellationToken) is { } feed ? feed : NotFoundUser(externalUserId);
    }

    private ObjectResult NotFoundUser(string externalUserId) =>
        this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound, $"User {externalUserId} not found.", new { externalUserId });
}
