using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Worker.Events.Kafka;

namespace FeedFabric.Worker.Tests;

[Collection("worker")]
public class HealthCheckTests(WorkerFixture fixture)
{
    private static HealthCheckContext Context(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration("test", check, HealthStatus.Unhealthy, null),
    };

    [Fact]
    public async Task Kafka_is_ready_once_the_topics_exist()
    {
        var options = new KafkaOptions { BootstrapServers = fixture.BootstrapServers, ReplicationFactor = 1, Partitions = 1 };
        using var check = new KafkaHealthCheck(Options.Create(options));
        await KafkaTopics.EnsureCreatedAsync(options);

        var result = await check.CheckHealthAsync(Context(check), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Kafka_is_unhealthy_without_a_broker()
    {
        using var check = new KafkaHealthCheck(Options.Create(new KafkaOptions { BootstrapServers = "127.0.0.1:1" }));

        var result = await check.CheckHealthAsync(Context(check), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ClamAv_ping_is_false_when_clamd_is_not_there()
    {
        var scanner = new ClamAvScanner(Options.Create(new MalwareScanningOptions { ClamAv = new ClamAvOptions { Host = "127.0.0.1", Port = 1 } }));

        Assert.False(await scanner.PingAsync(TestContext.Current.CancellationToken));
    }
}
