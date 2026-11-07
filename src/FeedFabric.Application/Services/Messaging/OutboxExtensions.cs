using System.Diagnostics;
using System.Text.Json;

namespace FeedFabric.Application.Services.Messaging;

public static class OutboxExtensions
{
    // Queues an event to be saved with the caller's next SaveChanges, so the change and its event commit together
    public static void AddEvent<T>(this AppDbContext db, string type, string partitionKey, Guid actorId, T data, DateTime occurredAt)
    {
        var message = new OutboxMessageEntity
        {
            Type = type,
            PartitionKey = partitionKey,
            Payload = "",
            CreatedAt = occurredAt,
        };
        // Add fills in message.Id, which the payload needs as its EventId
        db.OutboxMessages.Add(message);

        message.Payload = JsonSerializer.Serialize(new EventEnvelope<T>
        {
            EventId = message.Id,
            Type = type,
            OccurredAt = occurredAt,
            ActorId = actorId,
            CorrelationId = Activity.Current?.TraceId.ToString() ?? message.Id.ToString(),
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null,
            TraceState = Activity.Current?.TraceStateString,
            Data = data,
        }, EventJson.Options);
    }
}
