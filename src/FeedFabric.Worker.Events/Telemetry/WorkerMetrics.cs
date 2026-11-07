using System.Diagnostics.Metrics;

namespace FeedFabric.Worker.Events;

// The worker's own metrics, exported with the rest by OpenTelemetry (meter "FeedFabric.Worker")
public static class WorkerMetrics
{
    public const string MeterName = "FeedFabric.Worker";

    private static readonly Meter Meter = new(MeterName);

    // Events a consumer gave up on; anything above zero needs a person (docs/runbooks.md)
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        "feedfabric.events.dead_lettered", "{event}", "Events sent to a dead-letter topic");

    // Outbox messages parked after Kafka:MaxPublishAttempts failed publishes
    public static readonly Counter<long> OutboxParked = Meter.CreateCounter<long>(
        "feedfabric.outbox.parked", "{message}", "Outbox messages parked after too many failed publishes");
}
