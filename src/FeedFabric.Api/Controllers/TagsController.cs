using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed/tags")]
public class TagsController(ITagService tagService) : ControllerBase
{
    // GET /userfeed/tags/trending?hours=24&limit=10: tags on the most recent posts, hashtags and inferred topics alike
    [HttpGet("trending")]
    public async Task<ActionResult<IReadOnlyList<TrendingTagResponse>>> Trending(
        [Range(1, 168, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int hours = 24,
        [Range(1, 50, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int limit = 10,
        CancellationToken cancellationToken = default) =>
        Ok(await tagService.TrendingAsync(hours, limit, cancellationToken));

    // GET /userfeed/tags/{tag}/posts?limit=20, then add &cursor=<nextCursor>; newest first
    [HttpGet("{tag}/posts")]
    public async Task<ActionResult<HomeFeedResponse>> Posts(string tag, [FromQuery] HomeFeedQuery query, CancellationToken cancellationToken) =>
        Ok(await tagService.PostsAsync(tag, query, cancellationToken));
}
