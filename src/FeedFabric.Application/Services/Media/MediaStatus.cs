namespace FeedFabric.Application.Services.Media;

// The status as the API writes it: "awaitingUpload", "processing", "ready" or "rejected"
public static class MediaStatusNames
{
    public static string Of(MediaStatus status) => status switch
    {
        MediaStatus.AwaitingUpload => "awaitingUpload",
        MediaStatus.Processing => "processing",
        MediaStatus.Ready => "ready",
        _ => "rejected",
    };
}

public enum MediaStatus
{
    // Upload URL issued; waiting for the client to upload and call complete
    AwaitingUpload,

    // In quarantine being checked, scanned and rebuilt by the worker
    Processing,

    // Scanned clean and rebuilt; can be attached to posts and served
    Ready,

    // Failed a check; RejectionReason says which. The upload has been deleted.
    Rejected,
}
