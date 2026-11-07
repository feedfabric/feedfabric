using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace FeedFabric.Worker.Events.Kafka;

// Reads one topic in one consumer group and hands each event to HandleAsync. Offsets are committed only after
// the handler succeeds or the event is dead-lettered, so a crash means the event is delivered again rather than lost.
//
// A failure is retried with backoff. One that says a dependency is down (TransientFailures: Postgres, Redis, ClamAV,
// a model not published yet) is retried for as long as it takes, since the event isn't at fault. Anything else is
// tried Kafka:MaxHandlerAttempts times, and a payload that doesn't parse once; then the event goes to the topic's
// dead-letter topic (<topic>.dead-letter) with why, and the partition moves on. A poison event can't stall it.
//
// Order: the events after a dead-lettered one, for the same key, are still handled. Handlers are idempotent and each
// event carries what it needs, so that is safe for everything except replaying it later: check the current state
// in Postgres first (docs/runbooks.md, replaying the dead-letter topic).
public abstract class EventConsumer(
    IServiceScopeFactory scopeFactory,
    KafkaOptions options,
    IProducer<string, string> producer,
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
            BootstrapServers = options.BootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };
        options.Security.ApplyTo(config);
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

                await HandleWithRetryAsync(result, stoppingToken);
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

    // Returns once the event is handled or dead-lettered
    private async Task HandleWithRetryAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var message = result.Message;
        var type = message.Headers.TryGetLastBytes(KafkaHeaders.Type, out var typeBytes) ? Encoding.UTF8.GetString(typeBytes) : null;
        var failures = 0;
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
                await DeadLetterAsync(result, type, ex, attempt, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var transient = TransientFailures.IsTransient(ex);
                if (!transient && ++failures >= options.MaxHandlerAttempts)
                {
                    await DeadLetterAsync(result, type, ex, attempt, cancellationToken);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Min(attempt - 1, 10)), MaxRetryDelay.TotalSeconds));
                logger.LogError(ex, "Handling {Type} event with key {Key} failed (attempt {Attempt}{Transient}); retrying in {Delay}",
                    type, message.Key, attempt, transient ? ", a dependency is down" : $" of {options.MaxHandlerAttempts}", delay);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    // Copies the event, headers and all, to the dead-letter topic with where it came from and why it failed.
    // Keeps trying until Kafka takes it: committing the offset without it would lose the event.
    private async Task DeadLetterAsync(ConsumeResult<string, string> result, string? type, Exception error, int attempts,
        CancellationToken cancellationToken)
    {
        var headers = new Headers();
        foreach (var header in result.Message.Headers)
        {
            headers.Add(header.Key, header.GetValueBytes());
        }
        Add(headers, KafkaHeaders.DeadLetterTopic, result.Topic);
        Add(headers, KafkaHeaders.DeadLetterPartition, result.Partition.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, KafkaHeaders.DeadLetterOffset, result.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, KafkaHeaders.DeadLetterGroup, GroupId);
        Add(headers, KafkaHeaders.DeadLetterAttempts, attempts.ToString(CultureInfo.InvariantCulture));
        Add(headers, KafkaHeaders.DeadLetterError, $"{error.GetType().Name}: {error.Message}");

        var topic = KafkaTopics.DeadLetter(result.Topic);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = headers,
                }, cancellationToken);
                break;
            }
            catch (ProduceException<string, string> ex)
            {
                logger.LogError(ex, "Dead-lettering {Type} event with key {Key} to {Topic} failed (attempt {Attempt})", type, result.Message.Key, topic, attempt);
                await Task.Delay(MaxRetryDelay, timeProvider, cancellationToken);
            }
        }

        WorkerMetrics.DeadLettered.Add(1, new KeyValuePair<string, object?>("topic", result.Topic), new KeyValuePair<string, object?>("group", GroupId));
        // Critical, so log-based alerting picks it up: someone needs to look at the event and replay or drop it
        logger.LogCritical(error, "Dead-lettered {Type} event with key {Key} from {Topic} [{Partition}] @{Offset} to {DeadLetterTopic} after {Attempts} attempts",
            type, result.Message.Key, result.Topic, result.Partition.Value, result.Offset.Value, topic, attempts);
    }

    private static void Add(Headers headers, string key, string value) => headers.Add(key, Encoding.UTF8.GetBytes(value));
}
