namespace FeedFabric.Application.Services.Messaging;

// Kafka can deliver an event more than once; each handler records the events it has finished
public class ProcessedEventEntity
{
    public required Guid EventId { get; set; }

    // Which handler processed it, so one handler failing doesn't stop the others
    public required string Consumer { get; set; }

    public DateTime ProcessedAt { get; set; }
}
