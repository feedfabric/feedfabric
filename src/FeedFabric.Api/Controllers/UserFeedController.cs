using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed")]
public class UserFeedController(IFeedService feedService, IHomeFeedService homeFeedService, ForYouService forYouService, RankingProfileStore profiles, IPostService postService, ICurrentUser currentUser) : ControllerBase
{
    // GET /userfeed?gt=&gte=&lt=&lte=&order=newest|oldest&limit=20, then add &cursor=<nextCursor> for the next page
    [HttpGet]
    public async Task<ActionResult<FeedResponse>> GetFeed([FromQuery] FeedQuery query, CancellationToken cancellationToken)
    {
        var feed = await feedService.GetFeedAsync(currentUser.ExternalUserId, query, cancellationToken);
        return feed is null ? ToProblem(PostResultStatus.UserNotFound) : Ok(feed);
    }

    // GET /userfeed/home?limit=20, then add &cursor=<nextCursor> for the next page: posts from the accounts you follow
    [HttpGet("home")]
    public async Task<ActionResult<HomeFeedResponse>> GetHomeFeed([FromQuery] HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var feed = await homeFeedService.GetHomeFeedAsync(currentUser.ExternalUserId, query, cancellationToken);
        return feed is null ? ToProblem(PostResultStatus.UserNotFound) : Ok(feed);
    }

    // GET /userfeed/foryou?limit=20&profile=<key>, then add &cursor=<nextCursor>: ranked for you, each post with why
    // it's there. profile picks a ranking profile (GET /ranking/profiles); without it, the configured default.
    // The first page ranks again; later pages keep the session's order.
    [HttpGet("foryou")]
    public async Task<ActionResult<ForYouResponse>> GetForYou([FromQuery] ForYouQuery query, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(query.Profile, cancellationToken);
        if (profile is null)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.ProfileNotFound, $"There's no ranking profile called '{query.Profile}'.",
                new { profile = query.Profile });
        }
        var feed = await forYouService.GetAsync(currentUser.ExternalUserId, query, profile, cancellationToken);
        return feed is null ? ToProblem(PostResultStatus.UserNotFound) : Ok(feed);
    }

    [HttpPost("posts")]
    public async Task<ActionResult<PostResponse>> CreatePost(PostRequest request, CancellationToken cancellationToken)
    {
        var result = await postService.CreateAsync(currentUser.ExternalUserId, request, cancellationToken);
        return result.Status == PostResultStatus.Ok
            ? StatusCode(StatusCodes.Status201Created, result.Post)
            : ToProblem(result.Status, result.Violation);
    }

    [HttpPut("posts/{id:guid}")]
    public async Task<ActionResult<PostResponse>> EditPost(Guid id, PostRequest request, CancellationToken cancellationToken)
    {
        var result = await postService.EditAsync(currentUser.ExternalUserId, id, request, cancellationToken);
        return result.Status == PostResultStatus.Ok ? Ok(result.Post) : ToProblem(result.Status, result.Violation);
    }

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken cancellationToken)
    {
        var result = await postService.DeleteAsync(currentUser.ExternalUserId, id, cancellationToken);
        return result.Status == PostResultStatus.Ok ? NoContent() : ToProblem(result.Status);
    }

    private ObjectResult ToProblem(PostResultStatus status, string? violation = null) => status switch
    {
        PostResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        PostResultStatus.NotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound,
            "Post not found."),
        PostResultStatus.Forbidden => this.ApiProblem(StatusCodes.Status403Forbidden, ErrorCodes.PostNotAuthor,
            "Only the author can change this post."),
        PostResultStatus.EditWindowClosed => this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.PostEditWindowClosed,
            $"Posts can only be edited within {PostService.EditWindow.TotalMinutes:0} minutes of posting.",
            new { editWindowMinutes = (int)PostService.EditWindow.TotalMinutes }),
        PostResultStatus.ContentViolation => this.ApiProblem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.ContentViolation,
            "This post breaks the community rules.", new { category = violation }),
        PostResultStatus.TooManyMedia => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.PostTooManyMedia,
            "Too many images on one post."),
        PostResultStatus.MediaNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.MediaNotFound,
            "An image wasn't found, isn't yours, or is already on another post."),
        PostResultStatus.MediaNotReady => this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.MediaNotReady,
            "An image hasn't finished scanning, or was rejected."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
