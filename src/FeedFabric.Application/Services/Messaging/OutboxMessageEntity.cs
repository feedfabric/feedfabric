namespace FeedFabric.Application.Services.Messaging;

// Written in the same SaveChanges as the change it describes; FeedFabric.Worker.Events publishes it to Kafka
public class OutboxMessageEntity : EntityBase
{
    // e.g. EventTypes.PostCreated
    public required string Type { get; set; }

    // Serialized EventEnvelope; its EventId is this row's Id
    public required string Payload { get; set; }

    // Kafka message key: events with the same key stay in order
    public required string PartitionKey { get; set; }

    public DateTime CreatedAt { get; set; }

    // Null until published
    public DateTime? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
