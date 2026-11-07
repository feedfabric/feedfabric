using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FeedFabric.Worker.Events;

// The worker's own spans and metrics, exported with the rest by OpenTelemetry (source and meter "FeedFabric.Worker")
public static class WorkerMetrics
{
    public const string Name = "FeedFabric.Worker";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    // Events a consumer gave up on; anything above zero needs a person (docs/runbooks.md)
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        "feedfabric.events.dead_lettered", "{event}", "Events sent to a dead-letter topic");

    // Outbox messages parked after Outbox:MaxPublishAttempts failed publishes
    public static readonly Counter<long> OutboxParked = Meter.CreateCounter<long>(
        "feedfabric.outbox.parked", "{message}", "Outbox messages parked after too many failed publishes");

    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "feedfabric.events.handled", "{event}", "Events consumers finished, by topic and group");

    // Last seen by the outbox publisher (OutboxPublisher)
    private static long outboxWaiting;
    private static double outboxOldestSeconds;

    // Per consumer group and partition: messages behind the partition's end, as of the last event read
    private static readonly ConcurrentDictionary<(string Group, string Topic, int Partition), long> ConsumerLag = new();

    static WorkerMetrics()
    {
        Meter.CreateObservableGauge("feedfabric.outbox.waiting", () => Interlocked.Read(ref outboxWaiting), "{message}",
            "Outbox messages waiting to be published");
        Meter.CreateObservableGauge("feedfabric.outbox.lag", () => Volatile.Read(ref outboxOldestSeconds), "s",
            "Age of the oldest outbox message waiting to be published");
        Meter.CreateObservableGauge("feedfabric.consumer.lag", () => ConsumerLag.Select(entry => new Measurement<long>(entry.Value,
                new KeyValuePair<string, object?>("group", entry.Key.Group),
                new KeyValuePair<string, object?>("topic", entry.Key.Topic),
                new KeyValuePair<string, object?>("partition", entry.Key.Partition))),
            "{message}", "Messages a consumer group is behind on a partition");
    }

    public static void RecordOutbox(long waiting, TimeSpan oldest)
    {
        Interlocked.Exchange(ref outboxWaiting, waiting);
        Volatile.Write(ref outboxOldestSeconds, oldest.TotalSeconds);
    }

    public static void RecordConsumerLag(string group, string topic, int partition, long lag) =>
        ConsumerLag[(group, topic, partition)] = Math.Max(0, lag);
}
