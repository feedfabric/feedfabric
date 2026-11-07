using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace FeedFabric.Worker.Tests;

// Postgres (migrated) and a single Kafka broker in containers, shared by every test in the "worker" collection
public sealed class WorkerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly KafkaContainer kafka = new KafkaBuilder("apache/kafka:4.0.0").Build();

    public string BootstrapServers => kafka.GetBootstrapAddress();

    public ServiceProvider Services { get; private set; } = null!;

    public IProducer<string, string> Producer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), kafka.StartAsync());
        Services = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()))
            .AddLogging()
            .BuildServiceProvider();
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }
        Producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
    }

    // Everything on a topic so far, waiting up to the timeout for at least `count` messages; a topic that doesn't
    // exist yet reads as empty
    public List<ConsumeResult<string, string>> ReadAll(string topic, int count, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"test-reader-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        var results = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + timeout;
        while (results.Count < count && DateTime.UtcNow < deadline)
        {
            try
            {
                if (consumer.Consume(TimeSpan.FromMilliseconds(250)) is { IsPartitionEOF: false, Message: not null } result)
                {
                    results.Add(result);
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // Nothing has been written to it yet
            }
        }
        consumer.Close();
        return results;
    }

    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public async ValueTask DisposeAsync()
    {
        Producer?.Dispose();
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), kafka.DisposeAsync().AsTask());
    }
}

[CollectionDefinition("worker")]
public sealed class WorkerCollection : ICollectionFixture<WorkerFixture>;
