using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Admin;

// Shapes the admin panel (web/admin) reads. Statuses are lowercase words: "active" / "suspended" for users,
// "visible" / "held" / "removed" / "deleted" for posts and comments.

public record AdminPage<T>(IReadOnlyList<T> Items, int Total);

public record AdminUserRow(
    string ExternalUserId, string Username, string? DisplayName, string? AvatarUrl, string Status, bool IsVerified, DateTime SyncedAt,
    int Posts, int Followers, int ReportsAgainst, int Flags);

public record AdminUserDetail(
    AdminUserRow User, int Following, int Comments, int Likes,
    IReadOnlyList<AdminPostRow> RecentPosts, IReadOnlyList<AdminCommentRow> RecentComments,
    IReadOnlyList<ModerationLogEntry> History, IReadOnlyList<AdminReportRow> ReportsAgainst);

public record AdminPostRow(
    Guid Id, UserRef Author, string Content, string Status, DateTime CreatedAt, DateTime? EditedAt,
    int Likes, int Comments, int Reposts, int Reports);

public record AdminCommentRow(
    Guid Id, Guid PostId, Guid? ParentCommentId, UserRef Author, string Content, string Status, DateTime CreatedAt, int Reports);

public record AdminPostDetail(
    AdminPostRow Post, IReadOnlyList<TagResponse> Tags, IReadOnlyList<PostMediaResponse> Media,
    IReadOnlyList<AdminCommentRow> Comments, IReadOnlyList<AdminReportRow> Reports, IReadOnlyList<ModerationLogEntry> History);

// One report: who, why, and what about. Excerpt and TargetStatus describe the post or comment as it is now.
public record AdminReportRow(
    Guid Id, string Reason, DateTime CreatedAt, UserRef Reporter, string Kind, Guid PostId, Guid? CommentId,
    UserRef TargetAuthor, string Excerpt, string TargetStatus);

public static class AdminStatus
{
    public static string Of(ContentStatus status, DateTime? deletedAt) =>
        deletedAt is not null ? "deleted" : status.ToString().ToLowerInvariant();

    public static string Excerpt(string text, int length = 280) => text.Length > length ? text[..(length - 1)] + "…" : text;
}
