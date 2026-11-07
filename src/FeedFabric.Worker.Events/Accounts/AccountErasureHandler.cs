using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;

namespace FeedFabric.Worker.Events.Accounts;

// The rest of an account deletion (AccountService.DeleteAsync already emptied their posts and comments and the
// profile): their likes, follows, favourites, mentions, reports, media and its files, their feed log, and everything
// about them in Redis. Every step reads what's left and deletes it, so a retry after a failure part way through
// picks up where it stopped, and redelivery is harmless.
public class AccountErasureHandler(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IBlobStorage storage,
    EmbeddingStore embeddings,
    ILogger<AccountErasureHandler> logger)
{
    private const int Batch = 1000;

    public async Task HandleAsync(EventEnvelope<UserDeletedEvent> envelope, CancellationToken cancellationToken)
    {
        var userId = envelope.Data.UserId;
        var member = (RedisValue)userId.ToString();
        var database = redis.GetDatabase();

        var postIds = await db.Posts.AsNoTracking().Where(p => p.AuthorId == userId).Select(p => p.Id).ToListAsync(cancellationToken);
        var postMembers = postIds.Select(id => (RedisValue)id.ToString()).ToArray();

        // Redis first, while the rows that say where to look are still there
        var liked = await db.Likes.AsNoTracking().Where(l => l.UserId == userId).Select(l => l.PostId).ToListAsync(cancellationToken);
        await InBatchesAsync(liked, (batch, postId) => batch.SetRemoveAsync(EngagementKeys.Likers(postId), member));

        var followers = await db.Follows.AsNoTracking().Where(f => f.FolloweeId == userId).Select(f => f.FollowerId).ToListAsync(cancellationToken);
        if (postMembers.Length > 0)
        {
            await InBatchesAsync(followers, (batch, followerId) => batch.SortedSetRemoveAsync(FeedKeys.ForUser(followerId), postMembers));
        }

        await InBatchesAsync(postIds, (batch, postId) => batch.KeyDeleteAsync(
            [EngagementKeys.Likers(postId), ImpressionKeys.Viewers(postId), ImpressionKeys.PostDwell(postId)]));
        foreach (var chunk in postMembers.Chunk(Batch))
        {
            await database.SortedSetRemoveAsync(ImpressionKeys.PostTotals, chunk);
            await database.SortedSetRemoveAsync(ImpressionKeys.OpenTotals, chunk);
        }
        await embeddings.RemoveAsync(postIds);

        await database.KeyDeleteAsync([FeedKeys.ForUser(userId), EngagementKeys.Affinity(userId), EngagementKeys.Topics(userId),
            $"hidden:{userId}", $"feedback:{userId}"]);
        // Short-lived keys with the user in the middle of the name: For You sessions, served and seen markers. Rare
        // enough (one scan per deletion) to look for rather than wait out.
        var affinityKeys = new List<RedisKey>();
        foreach (var server in redis.GetServers().Where(s => s.IsConnected && !s.IsReplica))
        {
            foreach (var pattern in new[] { $"foryou:{userId}:*", $"foryou-peek:{userId}:*", $"served:*:{userId}:*", $"imp:seen:{userId}:*",
                $"open:seen:{userId}:*" })
            {
                var keys = new List<RedisKey>();
                await foreach (var key in server.KeysAsync(database.Database, pattern, pageSize: Batch).WithCancellation(cancellationToken))
                {
                    keys.Add(key);
                }
                foreach (var chunk in keys.Chunk(Batch))
                {
                    await database.KeyDeleteAsync(chunk);
                }
            }
            await foreach (var key in server.KeysAsync(database.Database, "affinity:*", pageSize: Batch).WithCancellation(cancellationToken))
            {
                affinityKeys.Add(key);
            }
        }
        // Other people's liking for them as an author
        await InBatchesAsync(affinityKeys, (batch, key) => batch.SortedSetRemoveAsync(key, member));

        // Their photos: files, then where they're attached, then the rows
        var media = await db.Media.Where(m => m.OwnerId == userId).ToListAsync(cancellationToken);
        foreach (var m in media)
        {
            foreach (var key in new[] { m.QuarantineKey, m.LargeKey, m.ThumbnailKey }.OfType<string>())
            {
                await storage.DeleteAsync(key, cancellationToken);
            }
        }
        var mediaIds = media.Select(m => m.Id).ToList();
        await db.PostMedia.Where(p => mediaIds.Contains(p.MediaId) || db.Posts.Any(post => post.Id == p.PostId && post.AuthorId == userId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.Media.Where(m => m.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);

        // Rows only about them, or about their (now empty) posts
        await db.PostTags.Where(t => db.Posts.Any(p => p.Id == t.PostId && p.AuthorId == userId)).ExecuteDeleteAsync(cancellationToken);
        await db.Mentions.Where(m => m.MentionedUserId == userId || m.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Likes.Where(l => l.UserId == userId || db.Posts.Any(p => p.Id == l.PostId && p.AuthorId == userId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.Follows.Where(f => f.FollowerId == userId || f.FolloweeId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Favourites.Where(f => f.UserId == userId || f.FavouriteId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Reports.Where(r => r.ReporterId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.FeedLog.Where(f => f.ViewerId == userId || db.Posts.Any(p => p.Id == f.PostId && p.AuthorId == userId))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Erased account {UserId}: {Posts} posts' signals, {Media} media", userId, postIds.Count, media.Count);
    }

    private async Task InBatchesAsync<T>(IReadOnlyCollection<T> items, Func<IBatch, T, Task> write)
    {
        foreach (var chunk in items.Chunk(Batch))
        {
            var batch = redis.GetDatabase().CreateBatch();
            var writes = chunk.Select(item => write(batch, item)).ToList();
            batch.Execute();
            await Task.WhenAll(writes);
        }
    }
}
