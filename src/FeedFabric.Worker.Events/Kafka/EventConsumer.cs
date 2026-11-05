using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace FeedFabric.Worker.Events.Kafka;

// Reads one topic in one consumer group and hands each event to HandleAsync. Offsets are committed only after
// the handler succeeds, so a crash means the event is delivered again rather than lost.
public abstract class EventConsumer(
    IServiceScopeFactory scopeFactory,
    string bootstrapServers,
    TimeProvider timeProvider,
    ILogger logger) : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    protected abstract string Topic { get; }

    protected abstract string GroupId { get; }

    protected abstract Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken);

    // Consume blocks its thread, so run it off the thread that starts the host
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => ConsumeAsync(stoppingToken), stoppingToken);

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Consuming {Topic} failed", Topic);
                    continue;
                }

                await HandleWithRetryAsync(result.Message, stoppingToken);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Leaves the group cleanly, so its partitions move to another instance straight away
            consumer.Close();
        }
    }

    // Retries until the handler succeeds: skipping an event would leave state missing what it describes
    private async Task HandleWithRetryAsync(Message<string, string> message, CancellationToken cancellationToken)
    {
        var type = message.Headers.TryGetLastBytes(KafkaHeaders.Type, out var typeBytes) ? Encoding.UTF8.GetString(typeBytes) : null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await HandleAsync(type, message.Value, scope.ServiceProvider, cancellationToken);
                return;
            }
            catch (JsonException ex)
            {
                // Retrying can't fix a payload that doesn't parse
                logger.LogError(ex, "Skipping unreadable {Type} event with key {Key}", type, message.Key);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), MaxRetryDelay.TotalSeconds));
                logger.LogError(ex, "Handling {Type} event with key {Key} failed (attempt {Attempt}); retrying in {Delay}", type, message.Key, attempt, delay);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }
}
