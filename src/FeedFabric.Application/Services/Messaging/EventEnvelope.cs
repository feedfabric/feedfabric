using System.Text.Json;

namespace FeedFabric.Application.Services.Messaging;

// Common fields on every event, so one user action can be traced across every service it touches
public class EventEnvelope<T>
{
    // Matches OutboxMessageEntity.Id
    public required Guid EventId { get; set; }

    public required string Type { get; set; }

    // Schema version of Data; bump it when Data changes in a way old consumers can't read
    public int Version { get; set; } = 1;

    public DateTime OccurredAt { get; set; }

    // User who caused the event
    public Guid ActorId { get; set; }

    // Client or API key
    public string? Source { get; set; }

    public required string CorrelationId { get; set; }

    // W3C trace context of the request that caused it; the outbox publisher copies it into the Kafka headers so the
    // worker's spans join the request's trace
    public string? TraceParent { get; set; }

    public string? TraceState { get; set; }

    public required T Data { get; set; }
}

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
