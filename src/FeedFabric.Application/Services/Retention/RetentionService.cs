using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Retention;

// Deletes what RetentionOptions says is past keeping. Each step works in small batches on an index (UUIDv7 IDs or a
// timestamp index), so it never holds long locks or reads a whole table, and running it twice is harmless.
public class RetentionService(
    AppDbContext db,
    IBlobStorage storage,
    IConnectionMultiplexer redis,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger)
{
    private const int BatchSize = 2000;

    public record Result(int Outbox, int ProcessedEvents, int FeedLog, int ModerationEvents, int Media, int PostTotals);

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var outboxBefore = Uuid7.LowerBound(now - settings.PublishedOutbox);
        var outbox = await DeleteInBatchesAsync(limit => db.Database.ExecuteSqlAsync($"""
            DELETE FROM "OutboxMessages" WHERE "Id" IN (
                SELECT "Id" FROM "OutboxMessages" WHERE "Id" < {outboxBefore} AND "ProcessedAt" IS NOT NULL LIMIT {limit})
            """, cancellationToken));

        // EventId is the outbox message's UUIDv7, so its age is the event's
        var eventsBefore = Uuid7.LowerBound(now - settings.ProcessedEvents);
        var processed = await DeleteInBatchesAsync(limit => db.Database.ExecuteSqlAsync($"""
            DELETE FROM "ProcessedEvents" WHERE ("EventId", "Consumer") IN (
                SELECT "EventId", "Consumer" FROM "ProcessedEvents" WHERE "EventId" < {eventsBefore} LIMIT {limit})
            """, cancellationToken));

        var servedBefore = now - settings.FeedLog;
        var feedLog = await DeleteInBatchesAsync(limit => db.Database.ExecuteSqlAsync($"""
            DELETE FROM "FeedLog" WHERE "Id" IN (SELECT "Id" FROM "FeedLog" WHERE "ServedAt" < {servedBefore} LIMIT {limit})
            """, cancellationToken));

        var moderatedBefore = now - settings.ModerationEvents;
        var blocked = ModerationActions.Blocked;
        var moderation = await DeleteInBatchesAsync(limit => db.Database.ExecuteSqlAsync($"""
            DELETE FROM "ModerationEvents" WHERE "Id" IN (
                SELECT "Id" FROM "ModerationEvents"
                WHERE "CreatedAt" < {moderatedBefore} AND ("Decision" IS NOT NULL OR "Action" = {blocked})
                LIMIT {limit})
            """, cancellationToken));

        var media = await DeleteMediaAsync(now - settings.AbandonedMedia, now - settings.RejectedMedia, cancellationToken);
        var totals = await TrimPostTotalsAsync(now - settings.PostTotals);
        await ExpireOldPostKeysAsync();

        var result = new Result(outbox, processed, feedLog, moderation, media, totals);
        logger.LogInformation("Retention deleted {Outbox} outbox messages, {ProcessedEvents} processed events, {FeedLog} feed log rows, " +
            "{ModerationEvents} moderation events, {Media} media and {PostTotals} posts from the Redis totals",
            outbox, processed, feedLog, moderation, media, totals);
        return result;
    }

    private static async Task<int> DeleteInBatchesAsync(Func<int, Task<int>> deleteBatch)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await deleteBatch(BatchSize);
            total += deleted;
        }
        while (deleted == BatchSize);
        return total;
    }

    // Upload URLs never used, ready photos never posted, and rejected uploads' records; files first, then the row
    private async Task<int> DeleteMediaAsync(DateTime abandonedBefore, DateTime rejectedBefore, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Media
                .Where(m => (m.Status == MediaStatus.AwaitingUpload && m.CreatedAt < abandonedBefore)
                    || (m.Status == MediaStatus.Ready && m.CreatedAt < abandonedBefore && !db.PostMedia.Any(p => p.MediaId == m.Id))
                    || (m.Status == MediaStatus.Rejected && m.CreatedAt < rejectedBefore))
                .OrderBy(m => m.Id)
                .Take(200)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return total;
            }
            foreach (var media in batch)
            {
                foreach (var key in new[] { media.QuarantineKey, media.LargeKey, media.ThumbnailKey })
                {
                    if (key is not null)
                    {
                        await storage.DeleteAsync(key, cancellationToken);
                    }
                }
            }
            db.Media.RemoveRange(batch);
            await db.SaveChangesAsync(cancellationToken);
            total += batch.Count;
        }
    }

    // Per-post signal keys written before they were given an expiry get one; once a day, since it scans the keyspace
    private async Task ExpireOldPostKeysAsync()
    {
        var database = redis.GetDatabase();
        if (!await database.StringSetAsync("retention:post-keys-swept", 1, TimeSpan.FromDays(1), When.NotExists))
        {
            return;
        }
        foreach (var server in redis.GetServers().Where(s => s.IsConnected && !s.IsReplica))
        {
            foreach (var pattern in new[] { "post:*:viewers", "post:*:dwell" })
            {
                await foreach (var key in server.KeysAsync(database.Database, pattern, pageSize: 1000))
                {
                    await database.KeyExpireAsync(key, ImpressionKeys.PostSignalLifetime, ExpireWhen.HasNoExpiry);
                }
            }
        }
    }

    // The totals are keyed by post ID, a UUIDv7, so a member's age is its post's
    private async Task<int> TrimPostTotalsAsync(DateTime before)
    {
        var database = redis.GetDatabase();
        var removed = 0;
        foreach (var key in new RedisKey[] { ImpressionKeys.PostTotals, ImpressionKeys.OpenTotals })
        {
            var old = new List<RedisValue>();
            await foreach (var entry in database.SortedSetScanAsync(key, pageSize: 1000))
            {
                if (Guid.TryParse(entry.Element.ToString(), out var id) && Uuid7.CreatedAt(id) < before)
                {
                    old.Add(entry.Element);
                }
            }
            foreach (var chunk in old.Chunk(1000))
            {
                removed += (int)await database.SortedSetRemoveAsync(key, chunk);
            }
        }
        return removed;
    }
}
