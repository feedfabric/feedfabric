using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Messaging;
using StackExchange.Redis;

namespace FeedFabric.Worker.Engagement;

// A comment says more than a like, so it adds CommentWeight to the commenter's affinity for the post's author,
// and to their interest in each of the post's tags (weighted by how sure the tag is).
// Deleting the comment leaves affinity alone: it records the interest the person showed at the time.
public class CommentHandler(AppDbContext db, IConnectionMultiplexer redis, TimeProvider timeProvider)
{
    public const string ConsumerName = "comment-affinity";
    private const int CommentWeight = 2;
    private static readonly TimeSpan MarkerLifetime = TimeSpan.FromDays(7);

    public async Task HandleAsync(EventEnvelope<CommentCreatedEvent> envelope, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await db.ProcessedEvents
            .AnyAsync(e => e.EventId == envelope.EventId && e.Consumer == ConsumerName, cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        // Commenting on your own post says nothing about who you like reading
        if (envelope.Data.PostAuthorId != envelope.ActorId)
        {
            // The marker makes the increment apply once even if the event is redelivered before ProcessedEvents is saved
            var tags = await db.PostTags.Where(t => t.PostId == envelope.Data.PostId)
                .Select(t => new { t.Tag, t.Confidence })
                .ToListAsync(cancellationToken);
            var marker = $"comment:{envelope.Data.CommentId}:counted";
            var transaction = redis.GetDatabase().CreateTransaction();
            transaction.AddCondition(Condition.KeyNotExists(marker));
            _ = transaction.StringSetAsync(marker, 1, MarkerLifetime);
            _ = transaction.SortedSetIncrementAsync(EngagementKeys.Affinity(envelope.ActorId), envelope.Data.PostAuthorId.ToString(), CommentWeight);
            foreach (var tag in tags)
            {
                _ = transaction.SortedSetIncrementAsync(EngagementKeys.Topics(envelope.ActorId), tag.Tag, CommentWeight * tag.Confidence);
            }
            await transaction.ExecuteAsync();
        }

        db.ProcessedEvents.Add(new ProcessedEventEntity
        {
            EventId = envelope.EventId,
            Consumer = ConsumerName,
            ProcessedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
