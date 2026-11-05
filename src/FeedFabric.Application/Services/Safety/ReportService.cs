using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Safety;

public class ReportRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [AllowedValues(ReportReasons.Spam, ReportReasons.Harassment, ReportReasons.Hate, ReportReasons.Threat, ReportReasons.Sexual,
        ReportReasons.SelfHarm, ReportReasons.Other, ErrorMessage = ErrorCodes.InvalidValue)]
    public string Reason { get; set; } = "";
}

public enum ReportResultStatus
{
    Ok,
    UserNotFound,
    PostNotFound,
    CommentNotFound,
}

// Members reporting posts and comments. Reporting twice counts once. Once ModerationOptions.ReportsToHold different
// people have reported something since a reviewer last looked at it, it's held and joins the review queue.
public class ReportService(AppDbContext db, IOptions<ModerationOptions> options, TimeProvider timeProvider)
{
    public async Task<ReportResultStatus> ReportPostAsync(string externalUserId, Guid postId, string reason, CancellationToken cancellationToken)
    {
        var reporterId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (reporterId is null)
        {
            return ReportResultStatus.UserNotFound;
        }

        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null && p.RepostOfId == null
            && (p.Status == ContentStatus.Visible || (p.Status == ContentStatus.Held && p.AuthorId == reporterId)), cancellationToken);
        if (post is null)
        {
            return ReportResultStatus.PostNotFound;
        }
        // Your own post: nothing to do
        if (post.AuthorId == reporterId)
        {
            return ReportResultStatus.Ok;
        }

        if (await AddAsync(reporterId.Value, postId, null, reason, cancellationToken) && post.Status == ContentStatus.Visible
            && await ReportsSinceReviewAsync(postId, null, cancellationToken) is var count && count >= options.Value.ReportsToHold)
        {
            post.Status = ContentStatus.Held;
            db.ModerationEvents.Add(Event(post.AuthorId, "post", post.Content, postId, null, count));
            await db.SaveChangesAsync(cancellationToken);
        }
        return ReportResultStatus.Ok;
    }

    public async Task<ReportResultStatus> ReportCommentAsync(string externalUserId, Guid commentId, string reason, CancellationToken cancellationToken)
    {
        var reporterId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (reporterId is null)
        {
            return ReportResultStatus.UserNotFound;
        }

        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.DeletedAt == null
            && (c.Status == ContentStatus.Visible || (c.Status == ContentStatus.Held && c.AuthorId == reporterId)), cancellationToken);
        if (comment is null)
        {
            return ReportResultStatus.CommentNotFound;
        }
        if (comment.AuthorId == reporterId)
        {
            return ReportResultStatus.Ok;
        }

        if (await AddAsync(reporterId.Value, comment.PostId, commentId, reason, cancellationToken) && comment.Status == ContentStatus.Visible
            && await ReportsSinceReviewAsync(comment.PostId, commentId, cancellationToken) is var count && count >= options.Value.ReportsToHold)
        {
            comment.Status = ContentStatus.Held;
            db.ModerationEvents.Add(Event(comment.AuthorId, "comment", comment.Content, comment.PostId, commentId, count));
            await db.SaveChangesAsync(cancellationToken);
        }
        return ReportResultStatus.Ok;
    }

    // False when this person had already reported it
    private async Task<bool> AddAsync(Guid reporterId, Guid postId, Guid? commentId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.CreateVersion7(timeProvider.GetUtcNow());
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "Reports" ("Id", "ReporterId", "PostId", "CommentId", "Reason", "CreatedAt")
            VALUES ({id}, {reporterId}, {postId}, {commentId}, {reason}, {now}) ON CONFLICT DO NOTHING
            """, cancellationToken) > 0;
    }

    // Reports since a reviewer last decided on it, so approving something resets its count
    private async Task<int> ReportsSinceReviewAsync(Guid postId, Guid? commentId, CancellationToken cancellationToken)
    {
        var lastReview = await db.ModerationEvents
            .Where(m => m.PostId == postId && m.CommentId == commentId && m.ReviewedAt != null)
            .MaxAsync(m => m.ReviewedAt, cancellationToken) ?? DateTime.MinValue;
        return await db.Reports.CountAsync(r => r.PostId == postId && r.CommentId == commentId && r.CreatedAt > lastReview, cancellationToken);
    }

    private ModerationEventEntity Event(Guid authorId, string kind, string text, Guid postId, Guid? commentId, int count) => new()
    {
        UserId = authorId,
        Kind = kind,
        Action = ModerationActions.Reported,
        Category = ModerationCategories.Reported,
        Score = count,
        Excerpt = ModerationEventEntity.ExcerptOf(text),
        PostId = postId,
        CommentId = commentId,
        CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
    };
}
