using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Admin;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// For the admin panel (web/admin): the review queue, reports and the moderation log
[ApiController]
[Authorize(Policy = ModerationPolicies.Reviewer)]
[Route("admin/moderation")]
public class AdminModerationController(ReviewService reviews, ICurrentUser currentUser) : ControllerBase
{
    // GET /admin/moderation/overview: what's waiting, and the last day's blocks, holds, reports and decisions
    [HttpGet("overview")]
    public Task<ModerationOverview> Overview(CancellationToken cancellationToken) => reviews.OverviewAsync(cancellationToken);

    // GET /admin/moderation/queue: held posts and comments, oldest first
    [HttpGet("queue")]
    public Task<IReadOnlyList<ReviewItemResponse>> Queue(CancellationToken cancellationToken) => reviews.QueueAsync(cancellationToken);

    // GET /admin/moderation/log?action=blocked|held|reported|reviewed&limit=100
    [HttpGet("log")]
    public Task<IReadOnlyList<ModerationLogEntry>> Log(string? action, [Range(1, 500, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        reviews.LogAsync(action, limit, cancellationToken);

    // GET /admin/moderation/reports?limit=100: the latest reports from members, newest first
    [HttpGet("reports")]
    public Task<IReadOnlyList<AdminReportRow>> Reports([Range(1, 500, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        reviews.ReportsAsync(limit, cancellationToken);

    // POST /admin/moderation/posts/{postId}/decision { "decision": "approved" | "removed" }
    [HttpPost("posts/{postId:guid}/decision")]
    public async Task<IActionResult> DecidePost(Guid postId, ReviewRequest request, CancellationToken cancellationToken) =>
        ToResult(await reviews.DecideAsync(currentUser.ExternalUserId, postId, null, request.Decision, cancellationToken));

    // POST /admin/moderation/comments/{commentId}/decision { "decision": "approved" | "removed" }
    [HttpPost("comments/{commentId:guid}/decision")]
    public async Task<IActionResult> DecideComment(Guid commentId, ReviewRequest request, CancellationToken cancellationToken) =>
        ToResult(await reviews.DecideAsync(currentUser.ExternalUserId, Guid.Empty, commentId, request.Decision, cancellationToken));

    private IActionResult ToResult(ReviewResultStatus status) => status switch
    {
        ReviewResultStatus.Ok => NoContent(),
        ReviewResultStatus.PostNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound, "Post not found."),
        ReviewResultStatus.CommentNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.CommentNotFound, "Comment not found."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
