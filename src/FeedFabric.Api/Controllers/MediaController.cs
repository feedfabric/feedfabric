using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// Image uploads: 1. POST /media/uploads for an upload URL. 2. PUT the file to it. 3. POST /media/{id}/complete.
// 4. Poll GET /media/{id} until "ready" (scanned and rebuilt) or "rejected". 5. Create the post with its mediaIds.
[ApiController]
[Authorize]
[Route("media")]
public class MediaController(IMediaService mediaService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("uploads")]
    public async Task<ActionResult<UploadResponse>> CreateUpload(CreateUploadRequest request, CancellationToken cancellationToken)
    {
        var result = await mediaService.CreateUploadAsync(currentUser.ExternalUserId, request, cancellationToken);
        return result.Status == MediaResultStatus.Ok ? StatusCode(StatusCodes.Status201Created, result.Value) : ToProblem(result.Status);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<MediaResponse>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediaService.CompleteAsync(currentUser.ExternalUserId, id, cancellationToken);
        return result.Status == MediaResultStatus.Ok ? Accepted(result.Value) : ToProblem(result.Status);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MediaResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediaService.GetAsync(currentUser.ExternalUserId, id, cancellationToken);
        return result.Status == MediaResultStatus.Ok ? Ok(result.Value) : ToProblem(result.Status);
    }

    private ObjectResult ToProblem(MediaResultStatus status) => status switch
    {
        MediaResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        MediaResultStatus.NotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.MediaNotFound, "Media not found."),
        MediaResultStatus.TypeNotAllowed => this.ApiProblem(StatusCodes.Status415UnsupportedMediaType, ErrorCodes.MediaTypeNotAllowed,
            "Only JPEG, PNG and WebP images can be uploaded.", new { allowed = MediaOptions.AllowedContentTypes }),
        MediaResultStatus.TooLarge => this.ApiProblem(StatusCodes.Status413PayloadTooLarge, ErrorCodes.MediaTooLarge,
            "The file is too large."),
        MediaResultStatus.WrongState => this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.MediaWrongState,
            "This upload has already been completed."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
