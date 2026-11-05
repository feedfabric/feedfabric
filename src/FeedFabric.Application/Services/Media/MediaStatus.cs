namespace FeedFabric.Application.Services.Media;

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
