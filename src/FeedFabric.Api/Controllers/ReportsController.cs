using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed")]
public class ReportsController(ReportService reports, ICurrentUser currentUser) : ControllerBase
{
    // POST /userfeed/posts/{postId}/report { "reason": "harassment" }; reporting twice counts once
    [HttpPost("posts/{postId:guid}/report")]
    public async Task<IActionResult> ReportPost(Guid postId, ReportRequest request, CancellationToken cancellationToken)
    {
        var status = await reports.ReportPostAsync(currentUser.ExternalUserId, postId, request.Reason, cancellationToken);
        return status == ReportResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    // POST /userfeed/comments/{commentId}/report { "reason": "spam" }
    [HttpPost("comments/{commentId:guid}/report")]
    public async Task<IActionResult> ReportComment(Guid commentId, ReportRequest request, CancellationToken cancellationToken)
    {
        var status = await reports.ReportCommentAsync(currentUser.ExternalUserId, commentId, request.Reason, cancellationToken);
        return status == ReportResultStatus.Ok ? NoContent() : ToProblem(status);
    }

    private ObjectResult ToProblem(ReportResultStatus status) => status switch
    {
        ReportResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        ReportResultStatus.PostNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound, "Post not found."),
        ReportResultStatus.CommentNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.CommentNotFound, "Comment not found."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
