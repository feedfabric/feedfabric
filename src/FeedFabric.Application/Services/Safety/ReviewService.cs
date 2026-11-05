using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Admin;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Safety;

public class ReviewRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [AllowedValues(ReviewDecisions.Approved, ReviewDecisions.Removed, ErrorMessage = ErrorCodes.InvalidValue)]
    public string Decision { get; set; } = "";
}

// One post or comment waiting for a reviewer, with everything that put it there
public class ReviewItemResponse
{
    // "post" or "comment"
    public required string Kind { get; set; }

    public required Guid PostId { get; set; }

    public Guid? CommentId { get; set; }

    public required UserRef Author { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; }

    // When it was first held
    public DateTime HeldAt { get; set; }

    public required IReadOnlyList<ReviewReason> Reasons { get; set; }

    // Reports since it was last reviewed, by reason
    public required IReadOnlyDictionary<string, int> Reports { get; set; }
}

public record UserRef(string ExternalUserId, string Username);

public record ReviewReason(string Action, string Category, double Score, DateTime At);

// One moderation event as the log shows it
// AuthorDeleted: its author deleted it before anyone reviewed it, so it's no longer waiting
public record ModerationLogEntry(
    Guid Id, string Kind, string Action, string Category, double Score, string? Excerpt, DateTime CreatedAt, UserRef Author,
    Guid? PostId, Guid? CommentId, string? Decision, string? ReviewedBy, DateTime? ReviewedAt, bool AuthorDeleted);

public record ModerationOverview(int Waiting, DateTime? OldestWaitingSince, int BlockedToday, int HeldToday, int FlaggedToday, int ReportsToday,
    int ReviewedToday, int RemovedToday);

public enum ReviewResultStatus
{
    Ok,
    PostNotFound,
    CommentNotFound,
}

// The review queue: held posts and comments, and the decisions on them. Each decision is stored on the events that
// put the item in the queue, which makes them labelled examples for retraining the classifier.
public class ReviewService(AppDbContext db, FeedLog feedLog, TimeProvider timeProvider)
{
    private const int MaxQueue = 200;

    public async Task<ModerationOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
        var open = Open();
        var today = await db.ModerationEvents.Where(m => m.CreatedAt >= since)
            .GroupBy(m => m.Action).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        // Posts and comments decided on, each by its latest decision; suspensions (Kind "user") aren't about either
        var decided = await db.ModerationEvents.Where(m => m.ReviewedAt >= since && m.PostId != null)
            .GroupBy(m => new { m.PostId, m.CommentId })
            .Select(g => g.OrderByDescending(m => m.ReviewedAt).Select(m => m.Decision).First())
            .ToListAsync(cancellationToken);
        return new ModerationOverview(
            await open.Select(m => new { m.PostId, m.CommentId }).Distinct().CountAsync(cancellationToken),
            await open.MinAsync(m => (DateTime?)m.CreatedAt, cancellationToken),
            today.GetValueOrDefault(ModerationActions.Blocked),
            today.GetValueOrDefault(ModerationActions.Held),
            today.GetValueOrDefault(ModerationActions.Flagged),
            await db.Reports.CountAsync(r => r.CreatedAt >= since, cancellationToken),
            decided.Count,
            decided.Count(d => d == ReviewDecisions.Removed));
    }

    // Oldest first, so nothing waits forever
    public async Task<IReadOnlyList<ReviewItemResponse>> QueueAsync(CancellationToken cancellationToken)
    {
        // MaxQueue items, not events: pick the oldest items first, then load all of their events
        var waiting = await Open()
            .GroupBy(m => new { m.PostId, m.CommentId })
            .Select(g => new { PostId = g.Key.PostId!.Value, g.Key.CommentId, HeldAt = g.Min(m => m.CreatedAt) })
            .OrderBy(x => x.HeldAt)
            .Take(MaxQueue)
            .ToListAsync(cancellationToken);
        var keys = waiting.Select(w => (w.PostId, w.CommentId)).ToHashSet();
        var waitingPostIds = keys.Select(k => k.PostId).Distinct().ToList();
        var open = await Open().AsNoTracking()
            .Where(m => waitingPostIds.Contains(m.PostId!.Value))
            .ToListAsync(cancellationToken);
        var groups = open.Where(m => keys.Contains((m.PostId!.Value, m.CommentId)))
            .GroupBy(m => (PostId: m.PostId!.Value, m.CommentId))
            .OrderBy(g => g.Min(m => m.CreatedAt))
            .ToList();

        var postIds = groups.Where(g => g.Key.CommentId is null).Select(g => g.Key.PostId).ToList();
        var commentIds = groups.Where(g => g.Key.CommentId is not null).Select(g => g.Key.CommentId!.Value).ToList();
        var posts = await db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id) && p.DeletedAt == null)
            .Join(db.Users, p => p.AuthorId, u => u.Id, (p, u) => new { p.Id, p.Content, p.CreatedAt, u.ExternalUserId, u.Username })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var comments = await db.Comments.AsNoTracking().Where(c => commentIds.Contains(c.Id) && c.DeletedAt == null)
            .Join(db.Users, c => c.AuthorId, u => u.Id, (c, u) => new { c.Id, c.Content, c.CreatedAt, u.ExternalUserId, u.Username })
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var reports = await db.Reports.AsNoTracking()
            .Where(r => postIds.Contains(r.PostId) && r.CommentId == null || r.CommentId != null && commentIds.Contains(r.CommentId.Value))
            // Only since a reviewer last decided on it
            .Where(r => !db.ModerationEvents.Any(m => m.PostId == r.PostId && m.CommentId == r.CommentId && m.ReviewedAt >= r.CreatedAt))
            .GroupBy(r => new { r.PostId, r.CommentId, r.Reason }).Select(g => new { g.Key.PostId, g.Key.CommentId, g.Key.Reason, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var items = new List<ReviewItemResponse>();
        foreach (var group in groups)
        {
            // Deleted by its author while waiting: nothing left to review
            var (content, createdAt, author) = group.Key.CommentId is { } commentId
                ? comments.TryGetValue(commentId, out var c) ? (c.Content, c.CreatedAt, new UserRef(c.ExternalUserId, c.Username)) : default
                : posts.TryGetValue(group.Key.PostId, out var p) ? (p.Content, p.CreatedAt, new UserRef(p.ExternalUserId, p.Username)) : default;
            if (content is null)
            {
                continue;
            }
            items.Add(new ReviewItemResponse
            {
                Kind = group.Key.CommentId is null ? "post" : "comment",
                PostId = group.Key.PostId,
                CommentId = group.Key.CommentId,
                Author = author,
                Content = content,
                CreatedAt = createdAt,
                HeldAt = group.Min(m => m.CreatedAt),
                Reasons = group.Select(m => new ReviewReason(m.Action, m.Category, m.Score, m.CreatedAt)).ToList(),
                Reports = reports.Where(r => r.PostId == group.Key.PostId && r.CommentId == group.Key.CommentId)
                    .ToDictionary(r => r.Reason, r => r.Count),
            });
        }
        return items;
    }

    // The latest events, newest first; action narrows it to blocked, held, reported or reviewed, and userId or postId
    // to one user's or one post's (its comments included)
    public async Task<IReadOnlyList<ModerationLogEntry>> LogAsync(string? action, int limit, CancellationToken cancellationToken,
        Guid? userId = null, Guid? postId = null)
    {
        var events = db.ModerationEvents.AsNoTracking();
        if (!string.IsNullOrEmpty(action))
        {
            events = events.Where(m => m.Action == action);
        }
        if (userId is { } user)
        {
            events = events.Where(m => m.UserId == user);
        }
        if (postId is { } post)
        {
            events = events.Where(m => m.PostId == post);
        }
        return await events.OrderByDescending(m => m.CreatedAt).Take(limit)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new ModerationLogEntry(
                m.Id, m.Kind, m.Action, m.Category, m.Score, m.Excerpt, m.CreatedAt, new UserRef(u.ExternalUserId, u.Username),
                m.PostId, m.CommentId, m.Decision, m.ReviewedBy, m.ReviewedAt,
                m.CommentId != null
                    ? db.Comments.Any(c => c.Id == m.CommentId && c.DeletedAt != null)
                    : m.PostId != null && db.Posts.Any(p => p.Id == m.PostId && p.DeletedAt != null)))
            .ToListAsync(cancellationToken);
    }

    // The latest reports, newest first, with what they're about as it is now. postId narrows it to one post and its
    // comments; targetAuthorId to reports about one user's posts and comments.
    public async Task<IReadOnlyList<AdminReportRow>> ReportsAsync(int limit, CancellationToken cancellationToken,
        Guid? postId = null, Guid? targetAuthorId = null)
    {
        var reports = db.Reports.AsNoTracking();
        if (postId is { } post)
        {
            reports = reports.Where(r => r.PostId == post);
        }
        if (targetAuthorId is { } author)
        {
            reports = reports.Where(r => r.CommentId == null
                ? db.Posts.Any(p => p.Id == r.PostId && p.AuthorId == author)
                : db.Comments.Any(c => c.Id == r.CommentId && c.AuthorId == author));
        }

        var rows = await reports.OrderByDescending(r => r.CreatedAt).Take(limit)
            .Join(db.Users, r => r.ReporterId, u => u.Id, (r, reporter) => new { r, reporter })
            .Join(db.Posts, x => x.r.PostId, p => p.Id, (x, p) => new { x.r, x.reporter, p })
            .Select(x => new
            {
                x.r.Id, x.r.Reason, x.r.CreatedAt, x.r.PostId, x.r.CommentId,
                Reporter = new UserRef(x.reporter.ExternalUserId, x.reporter.Username),
                Comment = db.Comments.Where(c => c.Id == x.r.CommentId)
                    .Select(c => new { c.Content, c.Status, c.DeletedAt, c.AuthorId }).FirstOrDefault(),
                Post = new { x.p.Content, x.p.Status, x.p.DeletedAt, x.p.AuthorId },
            })
            .ToListAsync(cancellationToken);
        var authorIds = rows.Select(r => r.Comment?.AuthorId ?? r.Post.AuthorId).Distinct().ToList();
        var authors = await db.Users.Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.ExternalUserId, u.Username), cancellationToken);
        return rows.Select(r => r.Comment is { } c
                ? new AdminReportRow(r.Id, r.Reason, r.CreatedAt, r.Reporter, "comment", r.PostId, r.CommentId, authors[c.AuthorId],
                    AdminStatus.Excerpt(c.Content, 160), AdminStatus.Of(c.Status, c.DeletedAt))
                : new AdminReportRow(r.Id, r.Reason, r.CreatedAt, r.Reporter, "post", r.PostId, null, authors[r.Post.AuthorId],
                    AdminStatus.Excerpt(r.Post.Content, 160), AdminStatus.Of(r.Post.Status, r.Post.DeletedAt)))
            .ToList();
    }

    // Waiting for a reviewer: held or reported, not yet decided, and not since deleted by its author
    private IQueryable<ModerationEventEntity> Open() =>
        db.ModerationEvents.Where(m => m.ReviewedAt == null && m.Action != ModerationActions.Blocked && m.PostId != null
            && (m.CommentId != null
                ? db.Comments.Any(c => c.Id == m.CommentId && c.DeletedAt == null)
                : db.Posts.Any(p => p.Id == m.PostId && p.DeletedAt == null)));

    // Approve shows it to everyone; remove hides it from everyone, its author included. Reviewers can also remove
    // something nobody flagged; that's recorded too.
    public async Task<ReviewResultStatus> DecideAsync(string reviewer, Guid postId, Guid? commentId, string decision, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var status = decision == ReviewDecisions.Approved ? ContentStatus.Visible : ContentStatus.Removed;
        Guid authorId;
        string kind, content;
        CommentEntity? comment = null;
        var wasHeld = false;
        if (commentId is { } id)
        {
            comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);
            if (comment is null)
            {
                return ReviewResultStatus.CommentNotFound;
            }
            wasHeld = comment.Status == ContentStatus.Held;
            (comment.Status, authorId, kind, content, postId) = (status, comment.AuthorId, "comment", comment.Content, comment.PostId);
        }
        else
        {
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null && p.RepostOfId == null, cancellationToken);
            if (post is null)
            {
                return ReviewResultStatus.PostNotFound;
            }
            (post.Status, authorId, kind, content) = (status, post.AuthorId, "post", post.Content);
        }

        var open = await db.ModerationEvents
            .Where(m => m.PostId == postId && m.CommentId == commentId && m.ReviewedAt == null && m.Action != ModerationActions.Blocked)
            .ToListAsync(cancellationToken);
        if (open.Count == 0 && decision == ReviewDecisions.Removed)
        {
            var manual = new ModerationEventEntity
            {
                UserId = authorId, Kind = kind, Action = ModerationActions.Reviewed, Category = ModerationCategories.Manual, Score = 0,
                Excerpt = ModerationEventEntity.ExcerptOf(content), PostId = postId, CommentId = commentId, CreatedAt = now,
            };
            db.ModerationEvents.Add(manual);
            open.Add(manual);
        }
        foreach (var m in open)
        {
            (m.Decision, m.ReviewedBy, m.ReviewedAt) = (decision, reviewer, now);
        }

        // A comment held when it was written was never counted as engagement (CommentService.CreateAsync): count it now.
        // One held later by reports was counted when it was written, so isn't counted again.
        var firstApproval = comment is not null && wasHeld && decision == ReviewDecisions.Approved
            && open.Any(m => m.Action == ModerationActions.Held);
        if (firstApproval)
        {
            var postAuthorId = await db.Posts.Where(p => p.Id == postId).Select(p => p.AuthorId).SingleAsync(cancellationToken);
            db.AddEvent(EventTypes.CommentCreated, postId.ToString(), comment!.AuthorId, CommentService.ToEvent(comment, postAuthorId), now);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (firstApproval)
        {
            await feedLog.MarkAsync(comment!.AuthorId, postId, FeedSignal.Commented, cancellationToken);
        }
        return ReviewResultStatus.Ok;
    }
}
