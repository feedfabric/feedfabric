using FeedFabric.Application.Services;

namespace FeedFabric.Application.Services.Media;

// An uploaded image. Clients only ever get URLs for the rebuilt copies, never for the upload itself.
public class MediaEntity : EntityBase
{
    public required Guid OwnerId { get; set; }

    public MediaStatus Status { get; set; }

    // What the client said it would upload; the worker checks the real type from the bytes
    public required string DeclaredContentType { get; set; }

    // Where the raw upload sits until it's been checked; deleted once processing finishes either way
    public required string QuarantineKey { get; set; }

    // Set when Ready: the rebuilt image and its thumbnail
    public string? LargeKey { get; set; }

    public string? ThumbnailKey { get; set; }

    public string? ContentType { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    // Set when Rejected; one of MediaRejections
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

// Which post a ready image is attached to, and in what order. An image belongs to at most one post.
public class PostMediaEntity
{
    public required Guid PostId { get; set; }

    public required Guid MediaId { get; set; }

    public int Position { get; set; }
}

public static class MediaRejections
{
    public const string UploadMissing = "upload_missing";
    public const string TooLarge = "too_large";
    public const string TypeNotAllowed = "type_not_allowed";
    public const string MalwareDetected = "malware_detected";
    public const string ImageTooBig = "image_dimensions_too_large";
    public const string Unreadable = "image_unreadable";
    public const string NudityDetected = "nudity_detected";
}
