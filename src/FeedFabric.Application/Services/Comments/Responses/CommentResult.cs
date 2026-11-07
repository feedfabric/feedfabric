namespace FeedFabric.Application.Services.Comments;

public enum CommentResultStatus
{
    Ok,
    UserNotFound,
    PostNotFound,
    CommentNotFound,
    Forbidden,
    // The text broke a community rule; CommentResult.Violation says which
    ContentViolation,
}

public record CommentResult(CommentResultStatus Status, CommentResponse? Comment = null, CommentPageResponse? Page = null, string? Violation = null);
