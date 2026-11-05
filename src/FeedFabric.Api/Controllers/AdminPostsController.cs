using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Admin;
using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Api.Controllers;

// Posts for moderators, in every status. Approve and remove with POST /admin/moderation/posts/{id}/decision.
[ApiController]
[Authorize(Policy = ModerationPolicies.Reviewer)]
[Route("admin/posts")]
public class AdminPostsController(AdminPostService posts, ReviewService reviews) : ControllerBase
{
    // GET /admin/posts?q=&author=&status=visible|held|removed|deleted&reported=true&offset=0&limit=50, newest first
    [HttpGet]
    public Task<AdminPage<AdminPostRow>> Search(string? q, string? author,
        [AllowedValues(null, "", "visible", "held", "removed", "deleted", ErrorMessage = ErrorCodes.InvalidValue)] string? status,
        bool reported = false, [Range(0, int.MaxValue)] int offset = 0,
        [Range(1, 200, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int limit = 50, CancellationToken cancellationToken = default) =>
        posts.SearchAsync(q, author, status, reported, offset, limit, cancellationToken);

    // GET /admin/posts/{postId}: with its comments, reports and moderation history
    [HttpGet("{postId:guid}")]
    public async Task<IActionResult> Detail(Guid postId, CancellationToken cancellationToken) =>
        await posts.DetailAsync(postId, reviews, cancellationToken) is { } detail
            ? Ok(detail)
            : this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound, "Post not found.");
}
