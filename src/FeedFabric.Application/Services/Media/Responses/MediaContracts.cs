namespace FeedFabric.Application.Services.Media;

public class UploadResponse
{
    public required Guid MediaId { get; init; }

    // PUT the file here with exactly this Content-Type, then call POST /media/{mediaId}/complete
    public required Uri UploadUrl { get; set; }

    public required string ContentType { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public class MediaResponse
{
    public required Guid Id { get; set; }

    // "awaitingUpload", "processing", "ready" or "rejected"
    public required string Status { get; set; }

    // Set when rejected; e.g. "malware_detected"
    public string? RejectionReason { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    // Short-lived URLs, only once ready
    public Uri? Url { get; set; }

    public Uri? ThumbnailUrl { get; set; }
}

// An image on a post, as feeds return it
public class PostMediaResponse
{
    public required Guid Id { get; set; }

    public required Uri Url { get; set; }

    public required Uri ThumbnailUrl { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public enum MediaResultStatus
{
    Ok,
    UserNotFound,
    NotFound,
    TypeNotAllowed,
    TooLarge,
    // complete was called when the media wasn't awaiting upload
    WrongState,
}

public record MediaResult<T>(MediaResultStatus Status, T? Value = default);
