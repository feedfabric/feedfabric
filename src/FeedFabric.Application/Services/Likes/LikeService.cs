using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Likes;

public class LikeService(AppDbContext db, FeedLog feedLog, TimeProvider timeProvider) : ILikeService
{
    public async Task<LikeResultStatus> LikeAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var (status, userId, authorId) = await FindAsync(externalUserId, postId, cancellationToken);
        if (status != LikeResultStatus.Ok)
        {
            return status;
        }

        // The like row and its event commit together; no event if the like was already there
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Likes" ("UserId", "PostId", "CreatedAt") VALUES ({userId}, {postId}, {now}) ON CONFLICT DO NOTHING""",
            cancellationToken);
        if (inserted > 0)
        {
            db.AddEvent(EventTypes.PostLiked, postId.ToString(), userId, new PostLikedEvent { PostId = postId, AuthorId = authorId }, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        if (inserted > 0)
        {
            await feedLog.MarkAsync(userId, postId, FeedSignal.Liked, cancellationToken);
        }

        return LikeResultStatus.Ok;
    }

    public async Task<LikeResultStatus> UnlikeAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var (status, userId, authorId) = await FindAsync(externalUserId, postId, cancellationToken);
        if (status != LikeResultStatus.Ok)
        {
            return status;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.Likes
            .Where(l => l.UserId == userId && l.PostId == postId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AddEvent(EventTypes.PostUnliked, postId.ToString(), userId, new PostLikedEvent { PostId = postId, AuthorId = authorId }, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return LikeResultStatus.Ok;
    }

    private async Task<(LikeResultStatus Status, Guid UserId, Guid AuthorId)> FindAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return (LikeResultStatus.UserNotFound, Guid.Empty, Guid.Empty);
        }

        // Only posts the user can see: visible ones, and their own held ones
        var authorId = await db.Posts
            .Where(p => p.Id == postId && p.DeletedAt == null && p.RepostOfId == null
                && (p.Status == ContentStatus.Visible || (p.Status == ContentStatus.Held && p.AuthorId == userId)))
            .Select(p => (Guid?)p.AuthorId)
            .SingleOrDefaultAsync(cancellationToken);
        return authorId is null
            ? (LikeResultStatus.PostNotFound, Guid.Empty, Guid.Empty)
            : (LikeResultStatus.Ok, userId.Value, authorId.Value);
    }
}
