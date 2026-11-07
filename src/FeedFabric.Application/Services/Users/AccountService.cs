using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Storage;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Users;

// A member's own account: deleting it (the right to erasure) and exporting everything held about them (subject
// access). docs/data-retention.md says what is kept, for how long, and what deletion leaves behind.
public class AccountService(AppDbContext db, IConnectionMultiplexer redis, IBlobStorage storage, TimeProvider timeProvider)
{
    private static readonly TimeSpan ExportUrlLifetime = TimeSpan.FromHours(1);

    // What a deleted account's row becomes. The row stays so other people's comments and reposts still point at
    // something, but nothing in it identifies the person: a new sign-in with the same token makes a new account.
    public static string DeletedExternalUserId(Guid userId) => $"deleted:{userId}";

    public static string DeletedUsername(Guid userId) => $"deleted_{userId.ToString("N")[^16..]}";

    // Takes their posts, comments and profile down at once, in one transaction, and leaves the rest (likes, follows,
    // mentions, reports, media and their files, the feed log, Redis) to the worker, through the outbox, so it
    // happens even if this server goes away straight afterwards. False if there's no such (undeleted) account.
    public async Task<bool> DeleteAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalUserId == externalUserId && u.Status != UserStatus.Deleted,
            cancellationToken);
        if (user is null)
        {
            return false;
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Posts.Where(p => p.AuthorId == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Content, "")
            .SetProperty(p => p.DeletedAt, p => p.DeletedAt ?? now), cancellationToken);
        // Comments stay as "deleted" placeholders, as when their author deletes one, so replies to them still read
        await db.Comments.Where(c => c.AuthorId == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Content, "")
            .SetProperty(c => c.DeletedAt, c => c.DeletedAt ?? now), cancellationToken);
        // The moderation log keeps what happened (a block, a suspension) but not what they wrote
        await db.ModerationEvents.Where(e => e.UserId == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Excerpt, (string?)null), cancellationToken);

        user.ExternalUserId = DeletedExternalUserId(user.Id);
        user.Username = DeletedUsername(user.Id);
        user.DisplayName = null;
        user.AvatarUrl = null;
        user.IsVerified = false;
        user.Status = UserStatus.Deleted;
        user.SyncedAt = now;
        db.AddEvent(EventTypes.UserDeleted, user.Id.ToString(), user.Id, new UserDeletedEvent { UserId = user.Id }, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Everything held about the member, as one JSON document. The feed log, the biggest part, is streamed rather
    // than loaded. Null if there's no such account.
    public async Task<AccountExport?> ExportAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.ExternalUserId == externalUserId, cancellationToken);
        if (user is null)
        {
            return null;
        }
        var id = user.Id;

        var posts = await db.Posts.AsNoTracking().Where(p => p.AuthorId == id).OrderBy(p => p.Id)
            .Select(p => new ExportedPost(p.Id, p.Content, p.CreatedAt, p.EditedAt, p.DeletedAt, p.RepostOfId, p.Status.ToString(),
                db.PostTags.Where(t => t.PostId == p.Id).Select(t => t.Tag).ToList()))
            .ToListAsync(cancellationToken);
        var comments = await db.Comments.AsNoTracking().Where(c => c.AuthorId == id).OrderBy(c => c.Id)
            .Select(c => new ExportedComment(c.Id, c.PostId, c.ParentCommentId, c.Content, c.CreatedAt, c.DeletedAt, c.Status.ToString()))
            .ToListAsync(cancellationToken);
        var likes = await db.Likes.AsNoTracking().Where(l => l.UserId == id).OrderBy(l => l.CreatedAt)
            .Select(l => new ExportedLike(l.PostId, l.CreatedAt)).ToListAsync(cancellationToken);
        var following = await db.Follows.AsNoTracking().Where(f => f.FollowerId == id)
            .Join(db.Users, f => f.FolloweeId, u => u.Id, (f, u) => new ExportedUserLink(u.ExternalUserId, u.Username, f.CreatedAt))
            .ToListAsync(cancellationToken);
        var followers = await db.Follows.AsNoTracking().Where(f => f.FolloweeId == id)
            .Join(db.Users, f => f.FollowerId, u => u.Id, (f, u) => new ExportedUserLink(u.ExternalUserId, u.Username, f.CreatedAt))
            .ToListAsync(cancellationToken);
        var favourites = await db.Favourites.AsNoTracking().Where(f => f.UserId == id)
            .Join(db.Users, f => f.FavouriteId, u => u.Id, (f, u) => new ExportedUserLink(u.ExternalUserId, u.Username, f.CreatedAt))
            .ToListAsync(cancellationToken);
        var mentions = await db.Mentions.AsNoTracking().Where(m => m.MentionedUserId == id)
            .Join(db.Users, m => m.AuthorId, u => u.Id, (m, u) => new ExportedMention(m.PostId, m.CommentId, u.Username, m.CreatedAt))
            .ToListAsync(cancellationToken);
        var reports = await db.Reports.AsNoTracking().Where(r => r.ReporterId == id)
            .Select(r => new ExportedReport(r.PostId, r.CommentId, r.Reason, r.CreatedAt)).ToListAsync(cancellationToken);
        var moderation = await db.ModerationEvents.AsNoTracking().Where(e => e.UserId == id).OrderBy(e => e.Id)
            .Select(e => new ExportedModerationEvent(e.Action, e.Kind, e.Category, e.Score, e.Excerpt, e.PostId, e.CommentId, e.CreatedAt,
                e.Decision, e.ReviewedAt))
            .ToListAsync(cancellationToken);

        var media = new List<ExportedMedia>();
        foreach (var m in await db.Media.AsNoTracking().Where(m => m.OwnerId == id).OrderBy(m => m.Id).ToListAsync(cancellationToken))
        {
            var url = m is { Status: MediaStatus.Ready, LargeKey: { } key }
                ? await storage.GetReadUrlAsync(key, ExportUrlLifetime, cancellationToken)
                : null;
            media.Add(new ExportedMedia(m.Id, m.Status.ToString(), m.RejectionReason, m.CreatedAt, url));
        }

        // What the ranker has learned about them; without Redis, the export still has everything else
        ExportedInterests? interests = null;
        try
        {
            var database = redis.GetDatabase();
            var authors = await database.SortedSetRangeByRankWithScoresAsync(EngagementKeys.Affinity(id), order: Order.Descending);
            var topics = await database.SortedSetRangeByRankWithScoresAsync(EngagementKeys.Topics(id), order: Order.Descending);
            var authorIds = authors.Select(a => Guid.TryParse(a.Element.ToString(), out var g) ? g : Guid.Empty).ToList();
            var names = await db.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);
            interests = new ExportedInterests(
                authors.Zip(authorIds).Where(a => names.ContainsKey(a.Second))
                    .Select(a => new ExportedScore(names[a.Second], a.First.Score)).ToList(),
                topics.Select(t => new ExportedScore(t.Element.ToString(), t.Score)).ToList());
        }
        catch (RedisException)
        {
        }

        var feedLog = db.FeedLog.AsNoTracking().Where(f => f.ViewerId == id).OrderBy(f => f.Id)
            .Select(f => new ExportedFeedEntry(f.PostId, f.Feed, f.Ranker, f.Profile, f.Position, f.ServedAt, f.Seen, f.Opened, f.Liked,
                f.Commented, f.DwellMs, f.NotInterested, f.ShowMore))
            .AsAsyncEnumerable();

        return new AccountExport(
            timeProvider.GetUtcNow().UtcDateTime,
            new ExportedUser(user.ExternalUserId, user.Username, user.DisplayName, user.AvatarUrl, user.Status.ToString(), user.IsVerified,
                Uuid7.CreatedAt(user.Id), user.SyncedAt),
            posts, comments, likes, following, followers, favourites, mentions, reports, media, moderation, interests, feedLog);
    }
}

public record AccountExport(
    DateTime ExportedAt,
    ExportedUser User,
    List<ExportedPost> Posts,
    List<ExportedComment> Comments,
    List<ExportedLike> Likes,
    List<ExportedUserLink> Following,
    List<ExportedUserLink> Followers,
    List<ExportedUserLink> Favourites,
    List<ExportedMention> MentionedIn,
    List<ExportedReport> ReportsMade,
    List<ExportedMedia> Media,
    List<ExportedModerationEvent> Moderation,
    // Null when Redis couldn't be read
    ExportedInterests? Interests,
    // Every post shown to them and what they did with it, kept for Retention:FeedLog
    IAsyncEnumerable<ExportedFeedEntry> FeedLog);

public record ExportedUser(string ExternalUserId, string Username, string? DisplayName, string? AvatarUrl, string Status, bool IsVerified,
    DateTime? CreatedAt, DateTime SyncedAt);

public record ExportedPost(Guid Id, string Content, DateTime CreatedAt, DateTime? EditedAt, DateTime? DeletedAt, Guid? RepostOf,
    string Status, List<string> Tags);

public record ExportedComment(Guid Id, Guid PostId, Guid? ParentCommentId, string Content, DateTime CreatedAt, DateTime? DeletedAt,
    string Status);

public record ExportedLike(Guid PostId, DateTime CreatedAt);

public record ExportedUserLink(string ExternalUserId, string Username, DateTime Since);

public record ExportedMention(Guid PostId, Guid? CommentId, string By, DateTime At);

public record ExportedReport(Guid PostId, Guid? CommentId, string Reason, DateTime CreatedAt);

// Url: a short-lived link to the image, for ready ones
public record ExportedMedia(Guid Id, string Status, string? RejectionReason, DateTime CreatedAt, Uri? Url);

public record ExportedModerationEvent(string Action, string Kind, string Category, double Score, string? Excerpt, Guid? PostId,
    Guid? CommentId, DateTime CreatedAt, string? Decision, DateTime? ReviewedAt);

// Authors (by username) and topics, scored by how much they've engaged with them
public record ExportedInterests(List<ExportedScore> Authors, List<ExportedScore> Topics);

public record ExportedScore(string Name, double Score);

public record ExportedFeedEntry(Guid PostId, string Feed, string Ranker, string? Profile, int Position, DateTime ServedAt, bool Seen,
    bool Opened, bool Liked, bool Commented, int DwellMs, bool NotInterested, bool ShowMore);
