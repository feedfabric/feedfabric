using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Posts;
using StackExchange.Redis;

namespace FeedFabric.Worker.Events.FanOut;

// Fan-out on write: adds a new post to the author's timeline and every follower's timeline in Redis
public class FeedFanOutHandler(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IOptions<FeedOptions> options,
    TimeProvider timeProvider)
{
    public const string ConsumerName = "feed-fan-out";
    private const int FollowerBatchSize = 1000;

    public async Task HandleAsync(EventEnvelope<PostCreatedEvent> envelope, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await db.ProcessedEvents
            .AnyAsync(e => e.EventId == envelope.EventId && e.Consumer == ConsumerName, cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        var post = envelope.Data;
        var member = post.PostId.ToString();
        var score = new DateTimeOffset(DateTime.SpecifyKind(post.CreatedAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        // The author sees their own post too
        await AddToTimelinesAsync([post.AuthorId], member, score);

        // Keyset paging by FollowerId uses the (FolloweeId, FollowerId) index and never loads every follower at once
        Guid? after = null;
        while (true)
        {
            var followers = db.Follows.AsNoTracking().Where(f => f.FolloweeId == post.AuthorId);
            if (after is { } last)
            {
                followers = followers.Where(f => f.FollowerId.CompareTo(last) > 0);
            }

            var followerIds = await followers
                .OrderBy(f => f.FollowerId)
                .Select(f => f.FollowerId)
                .Take(FollowerBatchSize)
                .ToListAsync(cancellationToken);
            if (followerIds.Count == 0)
            {
                break;
            }

            await AddToTimelinesAsync(followerIds, member, score);
            if (followerIds.Count < FollowerBatchSize)
            {
                break;
            }
            after = followerIds[^1];
        }

        // Redis writes can't roll back, but repeating them is harmless: ZADD of the same member just sets the same score
        db.ProcessedEvents.Add(new ProcessedEventEntity
        {
            EventId = envelope.EventId,
            Consumer = ConsumerName,
            ProcessedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    // One round trip per batch: add the post, then trim each timeline to the newest MaxLength entries
    private async Task AddToTimelinesAsync(IReadOnlyList<Guid> userIds, string member, long score)
    {
        var batch = redis.GetDatabase().CreateBatch();
        var writes = new List<Task>(userIds.Count * 2);
        foreach (var userId in userIds)
        {
            var key = FeedKeys.ForUser(userId);
            writes.Add(batch.SortedSetAddAsync(key, member, score));
            writes.Add(batch.SortedSetRemoveRangeByRankAsync(key, 0, -(options.Value.MaxLength + 1)));
        }
        batch.Execute();
        await Task.WhenAll(writes);
    }
}
