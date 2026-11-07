using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Worker.Events.Kafka;

namespace FeedFabric.Worker.Events.Outbox;

// Publishes unprocessed OutboxMessages to Kafka and marks them processed.
//
// Order: events with the same PartitionKey reach Kafka in the order they were written. Within a batch each key's
// messages go one after another and stop at the first failure, so a later one never overtakes one that failed; it
// waits for the next round. One instance publishes at a time (an advisory lock); other workers' publishers stand by.
//
// A message that fails Outbox:MaxPublishAttempts times is parked: skipped from then on, so the messages behind it
// for the same key carry on. That breaks their order on purpose, since one bad message mustn't stop a key forever;
// it's logged as critical and counted (WorkerMetrics.OutboxParked, and the admin panel's pipeline).
public class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IProducer<string, string> producer,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private DateTimeOffset lagRecordedAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    // pg_try_advisory_xact_lock key: any constant no other code uses
    private const long PublisherLock = 0x4645_4544_4F42; // "FEEDOB"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Publishing the outbox failed");
            }

            // A full batch means there is probably more waiting, so go again straight away
            if (published < BatchSize)
            {
                await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Held until the transaction ends; if another instance has it, it's publishing and this one waits a round
        var locked = await db.Database.SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock({PublisherLock}) AS "Value" """)
            .SingleAsync(cancellationToken);
        if (!locked)
        {
            return 0;
        }

        var maxAttempts = options.Value.MaxPublishAttempts;
        var messages = await db.OutboxMessages
            .FromSql($"""SELECT * FROM "OutboxMessages" WHERE "ProcessedAt" IS NULL AND "Attempts" < {maxAttempts} ORDER BY "CreatedAt", "Id" LIMIT {BatchSize} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        await RecordLagAsync(db, maxAttempts, cancellationToken);
        if (messages.Count == 0)
        {
            return 0;
        }

        // Keys in parallel, each key's messages in order
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await Task.WhenAll(messages.GroupBy(m => m.PartitionKey)
            .Select(key => PublishInOrderAsync(key.ToList(), now, maxAttempts, cancellationToken)));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    // The outbox gauges (WorkerMetrics), read at most every 15 seconds
    private async Task RecordLagAsync(AppDbContext db, int maxAttempts, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now - lagRecordedAt < TimeSpan.FromSeconds(15))
        {
            return;
        }
        lagRecordedAt = now;
        var waiting = await db.OutboxMessages.Waiting(maxAttempts).CountAsync(cancellationToken);
        var oldest = await db.OutboxMessages.Waiting(maxAttempts).MinAsync(o => (DateTime?)o.CreatedAt, cancellationToken);
        WorkerMetrics.RecordOutbox(waiting, oldest is null ? TimeSpan.Zero : now.UtcDateTime - oldest.Value);
    }

    private async Task PublishInOrderAsync(List<OutboxMessageEntity> messages, DateTime now, int maxAttempts, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            message.Attempts++;
            try
            {
                await ProduceAsync(message, cancellationToken);
                message.ProcessedAt = now;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is ProduceException<string, string> or ArgumentOutOfRangeException)
            {
                message.LastError = ex.Message;
                if (message.Attempts >= maxAttempts)
                {
                    WorkerMetrics.OutboxParked.Add(1, new KeyValuePair<string, object?>("type", message.Type));
                    logger.LogCritical(ex, "Parked {Type} {MessageId} with key {Key} after {Attempts} failed publishes",
                        message.Type, message.Id, message.PartitionKey, message.Attempts);
                }
                else
                {
                    logger.LogWarning(ex, "Publishing {Type} {MessageId} failed (attempt {Attempt} of {MaxAttempts})",
                        message.Type, message.Id, message.Attempts, maxAttempts);
                }
                // The rest of this key's messages wait, so none overtakes this one
                return;
            }
        }
    }

    private async Task ProduceAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        // Inside the async method, so an unknown type faults this message's task instead of the whole batch
        var topic = Topics.ForEventType(message.Type);
        var headers = new Headers { { KafkaHeaders.Type, Encoding.UTF8.GetBytes(message.Type) } };

        // The publish joins the trace of the request that wrote the event, and passes it on to the consumers
        var (traceParent, traceState) = TraceContext(message.Payload);
        ActivityContext.TryParse(traceParent, traceState, out var parent);
        using var activity = WorkerMetrics.Source.StartActivity($"{topic} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("feedfabric.event.type", message.Type);
        if ((activity?.Id ?? traceParent) is { } id)
        {
            headers.Add(KafkaHeaders.TraceParent, Encoding.UTF8.GetBytes(id));
            if ((activity?.TraceStateString ?? traceState) is { Length: > 0 } state)
            {
                headers.Add(KafkaHeaders.TraceState, Encoding.UTF8.GetBytes(state));
            }
        }

        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = message.PartitionKey,
            Value = message.Payload,
            Headers = headers,
        }, cancellationToken);
    }

    // EventEnvelope's traceParent and traceState, without deserializing Data
    private static (string? TraceParent, string? TraceState) TraceContext(string payload)
    {
        try
        {
            using var envelope = JsonDocument.Parse(payload);
            var root = envelope.RootElement;
            return (root.TryGetProperty("traceParent", out var parent) ? parent.GetString() : null,
                root.TryGetProperty("traceState", out var state) ? state.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
