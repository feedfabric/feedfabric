using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Messaging;
using StackExchange.Redis;

namespace FeedFabric.Worker.Engagement;

// Keeps three things in Redis per like: the set of likers of each post (its size is the like count),
// a per-viewer sorted set of how many posts they've liked from each author (affinity), and a per-viewer
// sorted set of interest in each of the post's tags, weighted by how sure the tag is.
public class LikeHandler(AppDbContext db, IConnectionMultiplexer redis, TimeProvider timeProvider)
{
    public const string ConsumerName = "like-counts";

    public async Task HandleAsync(EventEnvelope<PostLikedEvent> envelope, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await db.ProcessedEvents
            .AnyAsync(e => e.EventId == envelope.EventId && e.Consumer == ConsumerName, cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        var liked = envelope.Type == EventTypes.PostLiked;
        var likers = EngagementKeys.Likers(envelope.Data.PostId);
        var liker = (RedisValue)envelope.ActorId.ToString();
        var tags = await db.PostTags.Where(t => t.PostId == envelope.Data.PostId)
            .Select(t => new { t.Tag, t.Confidence })
            .ToListAsync(cancellationToken);

        // One atomic step that only applies if it changes the set, so redelivery can't count a like twice
        var transaction = redis.GetDatabase().CreateTransaction();
        transaction.AddCondition(liked ? Condition.SetNotContains(likers, liker) : Condition.SetContains(likers, liker));
        _ = liked ? transaction.SetAddAsync(likers, liker) : transaction.SetRemoveAsync(likers, liker);
        _ = transaction.SortedSetIncrementAsync(EngagementKeys.Affinity(envelope.ActorId), envelope.Data.AuthorId.ToString(), liked ? 1 : -1);
        foreach (var tag in tags)
        {
            _ = transaction.SortedSetIncrementAsync(EngagementKeys.Topics(envelope.ActorId), tag.Tag, liked ? tag.Confidence : -tag.Confidence);
        }
        await transaction.ExecuteAsync();

        db.ProcessedEvents.Add(new ProcessedEventEntity
        {
            EventId = envelope.EventId,
            Consumer = ConsumerName,
            ProcessedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
