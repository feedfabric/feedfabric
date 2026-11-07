using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;
using Npgsql;

namespace FeedFabric.Application.Services.Reposts;

// A repost is a post row with no content and RepostOfId set, so fan-out puts it in followers' timelines like any
// other post. Undoing it soft-deletes the row, and timelines drop it on read.
public class RepostService(AppDbContext db, TimeProvider timeProvider) : IRepostService
{
    public async Task<RepostResultStatus> RepostAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var (status, userId, originalId) = await FindAsync(externalUserId, postId, cancellationToken);
        if (status != RepostResultStatus.Ok)
        {
            return status;
        }

        if (await db.Posts.AnyAsync(p => p.AuthorId == userId && p.RepostOfId == originalId && p.DeletedAt == null, cancellationToken))
        {
            return RepostResultStatus.Ok;
        }

        var repost = new PostEntity
        {
            AuthorId = userId,
            Content = "",
            RepostOfId = originalId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.Posts.Add(repost);
        // Same SaveChanges as the repost, so fan-out sees it exactly when it exists
        db.AddEvent(EventTypes.PostCreated, repost.Id.ToString(), userId, new PostCreatedEvent
        {
            PostId = repost.Id,
            AuthorId = userId,
            CreatedAt = repost.CreatedAt,
        }, repost.CreatedAt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request reposted it first
            db.ChangeTracker.Clear();
        }

        return RepostResultStatus.Ok;
    }

    public async Task<RepostResultStatus> UnrepostAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var (status, userId, originalId) = await FindAsync(externalUserId, postId, cancellationToken);
        if (status != RepostResultStatus.Ok)
        {
            return status;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.Posts
            .Where(p => p.AuthorId == userId && p.RepostOfId == originalId && p.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, now), cancellationToken);

        return RepostResultStatus.Ok;
    }

    // The live, visible original behind postId, whether postId is the original or a repost of it
    private async Task<(RepostResultStatus Status, Guid UserId, Guid OriginalId)> FindAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return (RepostResultStatus.UserNotFound, Guid.Empty, Guid.Empty);
        }

        var originalId = await db.Posts
            .Where(p => p.Id == postId)
            .Select(p => p.RepostOfId ?? p.Id)
            .Where(id => db.Posts.Any(o => o.Id == id && o.DeletedAt == null && o.Status == ContentStatus.Visible))
            .Select(id => (Guid?)id)
            .SingleOrDefaultAsync(cancellationToken);
        return originalId is null
            ? (RepostResultStatus.PostNotFound, Guid.Empty, default)
            : (RepostResultStatus.Ok, userId.Value, originalId.Value);
    }
}
