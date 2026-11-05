using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Worker.Events.Kafka;

namespace FeedFabric.Worker.Events.Outbox;

// Publishes unprocessed OutboxMessages to Kafka and marks them processed
public class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IProducer<string, string> producer,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

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

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED lets several worker instances publish at once without taking the same rows
        var messages = await db.OutboxMessages
            .FromSql($"""SELECT * FROM "OutboxMessages" WHERE "ProcessedAt" IS NULL ORDER BY "CreatedAt", "Id" LIMIT {BatchSize} FOR UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return 0;
        }

        // Send the whole batch before waiting; the idempotent producer keeps each key's messages in order
        var deliveries = messages.Select(m => ProduceAsync(m, cancellationToken)).ToList();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            message.Attempts++;
            try
            {
                await deliveries[i];
                message.ProcessedAt = now;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is ProduceException<string, string> or ArgumentOutOfRangeException)
            {
                message.LastError = ex.Message;
                logger.LogWarning(ex, "Publishing {Type} {MessageId} failed", message.Type, message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    private async Task ProduceAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        // Inside the async method, so an unknown type faults this message's task instead of the whole batch
        var topic = Topics.ForEventType(message.Type);
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = message.PartitionKey,
            Value = message.Payload,
            Headers = new Headers { { KafkaHeaders.Type, Encoding.UTF8.GetBytes(message.Type) } },
        }, cancellationToken);
    }
}
