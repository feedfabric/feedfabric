using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Comments;

public class CommentService(AppDbContext db, FeedLog feedLog, TextModerator moderator, TimeProvider timeProvider) : ICommentService
{
    public async Task<CommentResult> ListAsync(Guid postId, CommentQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == postId && p.DeletedAt == null && p.RepostOfId == null, cancellationToken))
        {
            return new CommentResult(CommentResultStatus.PostNotFound);
        }

        var page = await PageAsync(db.Comments.Where(c => c.PostId == postId && c.ParentCommentId == null), query, cancellationToken);
        return new CommentResult(CommentResultStatus.Ok, Page: page);
    }

    public async Task<CommentResult> ListRepliesAsync(Guid commentId, CommentQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Comments.AnyAsync(c => c.Id == commentId, cancellationToken))
        {
            return new CommentResult(CommentResultStatus.CommentNotFound);
        }

        var page = await PageAsync(db.Comments.Where(c => c.ParentCommentId == commentId), query, cancellationToken);
        return new CommentResult(CommentResultStatus.Ok, Page: page);
    }

    public async Task<CommentResult> CreateAsync(string externalUserId, Guid postId, CommentRequest request, CancellationToken cancellationToken)
    {
        var authorId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (authorId is null)
        {
            return new CommentResult(CommentResultStatus.UserNotFound);
        }

        var postAuthorId = await db.Posts
            .Where(p => p.Id == postId && p.DeletedAt == null && p.RepostOfId == null)
            .Select(p => (Guid?)p.AuthorId)
            .SingleOrDefaultAsync(cancellationToken);
        if (postAuthorId is null)
        {
            return new CommentResult(CommentResultStatus.PostNotFound);
        }

        // A reply must be to a live comment on the same post
        if (request.ParentCommentId is { } parentId
            && !await db.Comments.AnyAsync(c => c.Id == parentId && c.PostId == postId && c.DeletedAt == null, cancellationToken))
        {
            return new CommentResult(CommentResultStatus.CommentNotFound);
        }

        if (await moderator.CheckAsync(request.Content, cancellationToken) is { } violation)
        {
            db.ModerationEvents.Add(new ModerationEventEntity
            {
                UserId = authorId.Value,
                Kind = "comment",
                Category = violation.Category,
                Score = violation.Score,
                Excerpt = request.Content.Length > 120 ? request.Content[..117] + "…" : request.Content,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(cancellationToken);
            return new CommentResult(CommentResultStatus.ContentViolation, Violation: violation.Category);
        }

        var comment = new CommentEntity
        {
            PostId = postId,
            AuthorId = authorId.Value,
            ParentCommentId = request.ParentCommentId,
            Content = request.Content,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.Comments.Add(comment);
        // Same SaveChanges as the comment, so the event exists exactly when the comment does
        db.AddEvent(EventTypes.CommentCreated, postId.ToString(), comment.AuthorId, ToEvent(comment, postAuthorId.Value), comment.CreatedAt);
        await db.AddMentionsAsync(comment.AuthorId, postId, comment.Id, comment.Content, comment.CreatedAt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await feedLog.MarkAsync(comment.AuthorId, postId, FeedSignal.Commented, cancellationToken);

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

    // Deleted comments only show while they still have replies to hold together
    private async Task<CommentPageResponse> PageAsync(IQueryable<CommentEntity> comments, CommentQuery query, CancellationToken cancellationToken)
    {
        comments = comments.Where(c => c.DeletedAt == null || db.Comments.Any(r => r.ParentCommentId == c.Id));
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
            Content = c.DeletedAt == null ? c.Content : null,
            IsDeleted = c.DeletedAt != null,
            CreatedAt = c.CreatedAt,
            ReplyCount = db.Comments.Count(r => r.ParentCommentId == c.Id && r.DeletedAt == null),
        });

    private static CommentCreatedEvent ToEvent(CommentEntity comment, Guid postAuthorId) => new()
    {
        CommentId = comment.Id,
        PostId = comment.PostId,
        PostAuthorId = postAuthorId,
        ParentCommentId = comment.ParentCommentId,
    };
}
