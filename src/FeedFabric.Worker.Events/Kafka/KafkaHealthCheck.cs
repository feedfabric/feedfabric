using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FeedFabric.Worker.Events.Kafka;

// Ready when a broker answers a metadata request and lists every topic this worker reads
public sealed class KafkaHealthCheck(IOptions<KafkaOptions> options) : IHealthCheck, IDisposable
{
    private readonly Lazy<IAdminClient> admin = new(() =>
    {
        var config = new AdminClientConfig { BootstrapServers = options.Value.BootstrapServers };
        options.Value.Security.ApplyTo(config);
        return new AdminClientBuilder(config).Build();
    });

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Synchronous in librdkafka; bounded by the timeout
            var metadata = admin.Value.GetMetadata(TimeSpan.FromSeconds(5));
            var missing = KafkaTopics.EventTopics.Except(metadata.Topics.Where(t => t.Error.Code == ErrorCode.NoError).Select(t => t.Topic)).ToList();
            return Task.FromResult(missing.Count == 0
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Missing topics: {string.Join(", ", missing)}."));
        }
        catch (KafkaException)
        {
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, "No Kafka broker is answering."));
        }
    }

    public void Dispose()
    {
        if (admin.IsValueCreated)
        {
            admin.Value.Dispose();
        }
    }
}
