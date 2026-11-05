using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Comments;

public class CommentService(AppDbContext db, FeedLog feedLog, TextModerator moderator, TimeProvider timeProvider) : ICommentService
{
    public async Task<CommentResult> ListAsync(string externalUserId, Guid postId, CommentQuery query, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (!await VisiblePosts(viewerId).AnyAsync(p => p.Id == postId, cancellationToken))
        {
            return new CommentResult(CommentResultStatus.PostNotFound);
        }

        var page = await PageAsync(viewerId, db.Comments.Where(c => c.PostId == postId && c.ParentCommentId == null), query, cancellationToken);
        return new CommentResult(CommentResultStatus.Ok, Page: page);
    }

    public async Task<CommentResult> ListRepliesAsync(string externalUserId, Guid commentId, CommentQuery query, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        // Its post must be one the viewer can read comments of, as in ListAsync
        var posts = VisiblePosts(viewerId);
        if (!await db.Comments.AnyAsync(c => c.Id == commentId && (c.Status != ContentStatus.Held || c.AuthorId == viewerId)
                && posts.Any(p => p.Id == c.PostId), cancellationToken))
        {
            return new CommentResult(CommentResultStatus.CommentNotFound);
        }

        var page = await PageAsync(viewerId, db.Comments.Where(c => c.ParentCommentId == commentId), query, cancellationToken);
        return new CommentResult(CommentResultStatus.Ok, Page: page);
    }

    // Posts the viewer can comment on and read comments of: visible ones, and their own held ones
    private IQueryable<PostEntity> VisiblePosts(Guid? viewerId) =>
        db.Posts.Where(p => p.DeletedAt == null && p.RepostOfId == null
            && (p.Status == ContentStatus.Visible || (p.Status == ContentStatus.Held && p.AuthorId == viewerId)));

    public async Task<CommentResult> CreateAsync(string externalUserId, Guid postId, CommentRequest request, CancellationToken cancellationToken)
    {
        var authorId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (authorId is null)
        {
            return new CommentResult(CommentResultStatus.UserNotFound);
        }

        var postAuthorId = await VisiblePosts(authorId)
            .Where(p => p.Id == postId)
            .Select(p => (Guid?)p.AuthorId)
            .SingleOrDefaultAsync(cancellationToken);
        if (postAuthorId is null)
        {
            return new CommentResult(CommentResultStatus.PostNotFound);
        }

        // A reply must be to a live comment on the same post
        if (request.ParentCommentId is { } parentId
            && !await db.Comments.AnyAsync(c => c.Id == parentId && c.PostId == postId && c.DeletedAt == null
                && (c.Status == ContentStatus.Visible || (c.Status == ContentStatus.Held && c.AuthorId == authorId)), cancellationToken))
        {
            return new CommentResult(CommentResultStatus.CommentNotFound);
        }

        var verdict = await moderator.CheckAsync(request.Content, cancellationToken);
        if (verdict is { Action: ModerationAction.Block })
        {
            db.ModerationEvents.Add(ModerationEventEntity.For(authorId.Value, "comment", verdict, request.Content, null, null, timeProvider.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync(cancellationToken);
            return new CommentResult(CommentResultStatus.ContentViolation, Violation: verdict.Category);
        }

        var comment = new CommentEntity
        {
            PostId = postId,
            AuthorId = authorId.Value,
            ParentCommentId = request.ParentCommentId,
            Content = request.Content,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Status = verdict is null ? ContentStatus.Visible : ContentStatus.Held,
        };
        db.Comments.Add(comment);
        if (verdict is not null)
        {
            // Saved with the comment, so a held comment is always in the review queue
            db.ModerationEvents.Add(ModerationEventEntity.For(comment.AuthorId, "comment", verdict, comment.Content, postId, comment.Id, comment.CreatedAt));
        }
        else
        {
            // Same SaveChanges as the comment, so the event exists exactly when the comment does. A held comment
            // counts as engagement only once a reviewer approves it (ReviewService.DecideAsync).
            db.AddEvent(EventTypes.CommentCreated, postId.ToString(), comment.AuthorId, ToEvent(comment, postAuthorId.Value), comment.CreatedAt);
        }
        await db.AddMentionsAsync(comment.AuthorId, postId, comment.Id, comment.Content, comment.CreatedAt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (verdict is null)
        {
            await feedLog.MarkAsync(comment.AuthorId, postId, FeedSignal.Commented, cancellationToken);
        }

        var created = await ToResponses(db.Comments.Where(c => c.Id == comment.Id)).SingleAsync(cancellationToken);
        return new CommentResult(CommentResultStatus.Ok, created);
    }

    public async Task<CommentResultStatus> DeleteAsync(string externalUserId, Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId, cancellationToken);
        if (comment is null)
        {
            return CommentResultStatus.CommentNotFound;
        }

        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (comment.AuthorId != userId)
        {
            return CommentResultStatus.Forbidden;
        }
        if (comment.DeletedAt is not null)
        {
            return CommentResultStatus.Ok;
        }

        comment.DeletedAt = timeProvider.GetUtcNow().UtcDateTime;
        var postAuthorId = await db.Posts.Where(p => p.Id == comment.PostId).Select(p => p.AuthorId).SingleAsync(cancellationToken);
        db.AddEvent(EventTypes.CommentDeleted, comment.PostId.ToString(), comment.AuthorId, ToEvent(comment, postAuthorId), comment.DeletedAt.Value);
        await db.SaveChangesAsync(cancellationToken);
        return CommentResultStatus.Ok;
    }

    // Deleted and removed comments only show while they still have replies to hold together. Held ones only show to
    // their author. Suspended accounts' comments don't show, except to their author.
    private async Task<CommentPageResponse> PageAsync(Guid? viewerId, IQueryable<CommentEntity> comments, CommentQuery query, CancellationToken cancellationToken)
    {
        comments = comments.Where(c => (c.Status != ContentStatus.Held || c.AuthorId == viewerId)
            && (c.AuthorId == viewerId || db.Users.Any(u => u.Id == c.AuthorId && u.Status == UserStatus.Active))
            && ((c.DeletedAt == null && c.Status != ContentStatus.Removed) || db.Comments.Any(r => r.ParentCommentId == c.Id)));
        if (query.Cursor is { } cursor)
        {
            comments = comments.Where(c => c.Id.CompareTo(cursor) > 0);
        }

        // One extra row tells us whether there is another page
        var page = await ToResponses(comments.OrderBy(c => c.Id)).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var results = page.Take(query.Limit).ToList();
        return new CommentPageResponse
        {
            Comments = results,
            NextCursor = page.Count > query.Limit ? results[^1].Id : null,
        };
    }

    private IQueryable<CommentResponse> ToResponses(IQueryable<CommentEntity> comments) =>
        comments.AsNoTracking().Join(db.Users, c => c.AuthorId, u => u.Id, (c, u) => new CommentResponse
        {
            Id = c.Id,
            PostId = c.PostId,
            ParentCommentId = c.ParentCommentId,
            AuthorId = c.AuthorId,
            AuthorExternalUserId = u.ExternalUserId,
            AuthorUsername = u.Username,
            Content = c.DeletedAt == null && c.Status != ContentStatus.Removed ? c.Content : null,
            IsDeleted = c.DeletedAt != null || c.Status == ContentStatus.Removed,
            IsHeld = c.Status == ContentStatus.Held,
            CreatedAt = c.CreatedAt,
            // Visible replies: a held one is only counted once it's approved
            ReplyCount = db.Comments.Count(r => r.ParentCommentId == c.Id && r.DeletedAt == null && r.Status == ContentStatus.Visible),
        });


    internal static CommentCreatedEvent ToEvent(CommentEntity comment, Guid postAuthorId) => new()
    {
        CommentId = comment.Id,
        PostId = comment.PostId,
        PostAuthorId = postAuthorId,
        ParentCommentId = comment.ParentCommentId,
    };
}
