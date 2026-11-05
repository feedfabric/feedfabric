namespace FeedFabric.Application.Services.Posts;

public enum PostResultStatus
{
    Ok,
    UserNotFound,
    NotFound,
    Forbidden,
    EditWindowClosed,
    // Reposts have no text of their own to edit
    IsRepost,
    TooManyMedia,
    // An image is missing, someone else's, or already on another post
    MediaNotFound,
    // An image hasn't finished scanning, or was rejected
    MediaNotReady,
    // The text broke a community rule; PostResult.Violation says which
    ContentViolation,
}

public record PostResult(PostResultStatus Status, PostResponse? Post = null, string? Violation = null)
{
    public static PostResult Ok(PostEntity post) => new(PostResultStatus.Ok, PostResponse.From(post));
}
