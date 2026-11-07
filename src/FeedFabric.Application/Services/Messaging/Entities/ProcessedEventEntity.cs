namespace FeedFabric.Application.Services.Messaging;

// Kafka can deliver an event more than once; each handler records the events it has finished
public class ProcessedEventEntity
{
    public required Guid EventId { get; init; }

    // Which handler processed it, so one handler failing doesn't stop the others
    public required string Consumer { get; init; }

    public DateTime ProcessedAt { get; init; }
}
