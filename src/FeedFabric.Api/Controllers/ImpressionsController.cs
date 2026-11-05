using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("userfeed")]
public class ImpressionsController(IImpressionService impressionService, ForYouService forYou, ICurrentUser currentUser) : ControllerBase
{
    // POST /userfeed/impressions { "postIds": [...] }: posts the user has seen (at least half on screen for a second).
    // Clients batch them; repeats within 30 minutes aren't counted again.
    [HttpPost("impressions")]
    public async Task<IActionResult> Record(ImpressionsRequest request, CancellationToken cancellationToken)
    {
        var counted = await impressionService.RecordAsync(currentUser.ExternalUserId, request.PostIds, cancellationToken);
        return counted is null ? NotSynced() : Accepted(new { counted });
    }

    // POST /userfeed/posts/{postId}/open: the user opened the post's own page. Repeats within 30 minutes aren't counted again.
    [HttpPost("posts/{postId:guid}/open")]
    public async Task<IActionResult> Open(Guid postId, CancellationToken cancellationToken)
    {
        var counted = await impressionService.RecordOpenAsync(currentUser.ExternalUserId, postId, cancellationToken);
        return counted is null ? NotSynced() : Accepted(new { counted });
    }

    // POST /userfeed/dwell { "views": [{ "postId", "ms" }] }: how long each post stayed on screen, sent as it scrolls away.
    // A view over 60 seconds counts as 60.
    [HttpPost("dwell")]
    public async Task<IActionResult> Dwell(DwellRequest request, CancellationToken cancellationToken)
    {
        var counted = await impressionService.RecordDwellAsync(currentUser.ExternalUserId,
            request.Views.Select(v => (v.PostId, v.Ms)).ToList(), cancellationToken);
        return counted is null ? NotSynced() : Accepted(new { counted });
    }

    // POST /userfeed/posts/{postId}/not-interested: the post never comes back in For You, and its author and topics
    // count against it for this user from now on
    [HttpPost("posts/{postId:guid}/not-interested")]
    public async Task<IActionResult> NotInterested(Guid postId, CancellationToken cancellationToken) =>
        await forYou.NotInterestedAsync(currentUser.ExternalUserId, postId, cancellationToken)
            ? NoContent()
            : this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound, "Post not found.");

    // POST /userfeed/posts/{postId}/show-more: the opposite of not-interested. Its author and topics count for it for
    // this user from now on, and a post they'd hidden can come back.
    [HttpPost("posts/{postId:guid}/show-more")]
    public async Task<IActionResult> ShowMore(Guid postId, CancellationToken cancellationToken) =>
        await forYou.ShowMoreAsync(currentUser.ExternalUserId, postId, cancellationToken)
            ? NoContent()
            : this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.PostNotFound, "Post not found.");

    private ObjectResult NotSynced() => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
        $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId });
}

public class DwellRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(100, ErrorMessage = ErrorCodes.OutOfRange + ";max={1}")]
    public List<DwellView> Views { get; set; } = [];
}

public class DwellView
{
    public Guid PostId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")]
    public int Ms { get; set; }
}

public class ImpressionsRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(100, ErrorMessage = ErrorCodes.OutOfRange + ";max={1}")]
    public List<Guid> PostIds { get; set; } = [];
}
