namespace FeedFabric.Application.Errors;

// Stable, machine-readable error codes sent as "code" in every error response.
// Clients translate these; the English "detail" is only a developer fallback. Never rename a code.
public static class ErrorCodes
{
    // Any request
    public const string ValidationFailed = "validation_failed";
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string UnsupportedMediaType = "unsupported_media_type";
    // Over the request body limit (Kestrel:Limits:MaxRequestBodySize)
    public const string RequestTooLarge = "request_too_large";
    // Too many requests of one kind in a short time (RateLimits); params: { retryAfterSeconds }, also in Retry-After
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";

    // Postgres, Redis or blob storage is unreachable for now; Retry-After says when to try again
    public const string ServiceUnavailable = "service_unavailable";

    // Users and posts
    public const string UserNotSynced = "user.not_synced";
    public const string UserSuspended = "user.suspended";
    public const string PostNotFound = "post.not_found";
    public const string PostNotAuthor = "post.not_author";
    public const string PostEditWindowClosed = "post.edit_window_closed";
    public const string PostIsRepost = "post.is_repost";

    // Comments
    public const string CommentNotFound = "comment.not_found";
    public const string CommentNotAuthor = "comment.not_author";

    // Media
    public const string MediaNotFound = "media.not_found";
    public const string MediaNotReady = "media.not_ready";
    public const string MediaTypeNotAllowed = "media.type_not_allowed";
    public const string MediaTooLarge = "media.too_large";
    public const string MediaWrongState = "media.wrong_state";
    public const string PostTooManyMedia = "post.too_many_media";

    // Safety: a post, comment or photo that broke a community rule; params: { category }
    public const string ContentViolation = "content.violation";

    // Follows
    public const string UserNotFound = "user.not_found";
    public const string FollowSelf = "follow.self";

    // Favourites
    public const string FavouriteSelf = "favourite.self";
    // Already at the most allowed; params: { max }
    public const string FavouriteLimitReached = "favourite.limit_reached";

    // Ranking profiles
    public const string ProfileNotFound = "profile.not_found";
    // Presets can't be changed or deleted; save a copy under a new key
    public const string ProfileReadOnly = "profile.read_only";
    // Settings out of range or a formula that doesn't parse; "errors" lists each { field, message, position? }
    public const string ProfileInvalid = "profile.invalid";
    // Keys are 2-40 lowercase letters, digits and dashes, starting with a letter
    public const string ProfileInvalidKey = "profile.invalid_key";

    // Field codes inside validation_failed "errors".
    // Validation attributes put them in ErrorMessage, with params as ";name={n}" placeholders.
    public const string Required = "required";
    public const string OutOfRange = "out_of_range";
    // Longer than allowed; params: { max } characters
    public const string TooLong = "too_long";
    public const string InvalidValue = "invalid_value";
}
