using FeedFabric.Application.Services.DataTypes;

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
    // data.type isn't a registered data type
    DataTypeUnknown,
    DataTooLarge,
    // The data doesn't match its type's schema; PostResult.DataErrors says how
    DataInvalid,
}

public record PostResult(PostResultStatus Status, PostResponse? Post = null, string? Violation = null, IReadOnlyList<DataError>? DataErrors = null)
{
    public static PostResult Ok(PostEntity post) => new(PostResultStatus.Ok, PostResponse.From(post));
}
