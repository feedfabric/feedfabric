using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Worker.Events.Kafka;

namespace FeedFabric.Worker.Tests;

[Collection("worker")]
public class EventConsumerTests(WorkerFixture fixture)
{
    // Fails "poison" every time, "flaky" with a dependency error for its first `transientFailures` tries
    private sealed class TestConsumer(WorkerFixture fixture, string topic, int maxAttempts, int transientFailures)
        : EventConsumer(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new KafkaOptions { BootstrapServers = fixture.BootstrapServers, MaxHandlerAttempts = maxAttempts },
            fixture.Producer, TimeProvider.System, WorkerFixture.Logger<TestConsumer>())
    {
        private int flakyTries;

        public ConcurrentQueue<string> Handled { get; } = new();

        protected override string Topic => topic;

        protected override string GroupId => $"test-{topic}";

        protected override Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken)
        {
            if (payload == "poison")
            {
                throw new InvalidOperationException("This event can never be handled.");
            }
            if (payload == "flaky" && Interlocked.Increment(ref flakyTries) <= transientFailures)
            {
                throw new MalwareScanException("clamd is down");
            }
            Handled.Enqueue(payload);
            return Task.CompletedTask;
        }
    }

    private async Task ProduceAsync(string topic, string key, params string[] values)
    {
        foreach (var value in values)
        {
            await fixture.Producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = key,
                Value = value,
                Headers = new Headers { { KafkaHeaders.Type, Encoding.UTF8.GetBytes("Test") } },
            }, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_poison_event_is_dead_lettered_and_the_partition_moves_on()
    {
        var topic = $"test.poison-{Guid.NewGuid():N}";
        await ProduceAsync(topic, "same-key", "poison", "after");
        var consumer = new TestConsumer(fixture, topic, maxAttempts: 2, transientFailures: 0);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => consumer.Handled.Contains("after"), TimeSpan.FromSeconds(60));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        var dead = Assert.Single(fixture.ReadAll(KafkaTopics.DeadLetter(topic), 1, TimeSpan.FromSeconds(20)));
        Assert.Equal("poison", dead.Message.Value);
        Assert.Equal("same-key", dead.Message.Key);
        string Header(string key) => Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes(key));
        Assert.Equal(topic, Header(KafkaHeaders.DeadLetterTopic));
        Assert.Equal("2", Header(KafkaHeaders.DeadLetterAttempts));
        Assert.Contains("never be handled", Header(KafkaHeaders.DeadLetterError));
        Assert.Equal("Test", Header(KafkaHeaders.Type));
    }

    [Fact]
    public async Task A_dependency_being_down_is_retried_rather_than_dead_lettered()
    {
        var topic = $"test.flaky-{Guid.NewGuid():N}";
        await ProduceAsync(topic, "key", "flaky");
        // More dependency failures than attempts allowed: they don't count
        var consumer = new TestConsumer(fixture, topic, maxAttempts: 1, transientFailures: 2);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => consumer.Handled.Contains("flaky"), TimeSpan.FromSeconds(60));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        Assert.Empty(fixture.ReadAll(KafkaTopics.DeadLetter(topic), 1, TimeSpan.FromSeconds(3)));
    }
}
