using System.Text.Json;

namespace FeedFabric.Application.Services.Messaging;

// Common fields on every event, so one user action can be traced across every service it touches
public class EventEnvelope<T>
{
    // Matches OutboxMessageEntity.Id
    public required Guid EventId { get; init; }

    public required string Type { get; init; }

    // Schema version of Data; bump it when Data changes in a way old consumers can't read
    public int Version { get; init; } = 1;

    public DateTime OccurredAt { get; init; }

    // User who caused the event
    public Guid ActorId { get; init; }

    // Client or API key
    public string? Source { get; init; }

    public required string CorrelationId { get; init; }

    // W3C trace context of the request that caused it; the outbox publisher copies it into the Kafka headers so the
    // worker's spans join the request's trace
    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    public required T Data { get; init; }
}

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
