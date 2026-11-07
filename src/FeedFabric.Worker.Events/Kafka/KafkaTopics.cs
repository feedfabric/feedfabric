using System.Globalization;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FeedFabric.Application.Services.Messaging;

namespace FeedFabric.Worker.Events.Kafka;

public static class KafkaTopics
{
    public static readonly string[] EventTopics = [Topics.Posts, Topics.Engagement, Topics.Media];

    // Where a consumer sends an event it gave up on (EventConsumer)
    public static string DeadLetter(string topic) => $"{topic}.dead-letter";

    // Creates the topics this worker uses if they don't exist yet, so the consumer never subscribes to a missing
    // topic. Existing topics are left as they are: change their settings with the broker's tools.
    public static async Task EnsureCreatedAsync(KafkaOptions options)
    {
        var config = new AdminClientConfig { BootstrapServers = options.BootstrapServers };
        options.Security.ApplyTo(config);
        using var admin = new AdminClientBuilder(config).Build();
        try
        {
            await admin.CreateTopicsAsync(EventTopics.Select(topic => Spec(topic, options, options.Retention))
                .Concat(EventTopics.Select(topic => Spec(DeadLetter(topic), options, options.DeadLetterRetention))));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private static TopicSpecification Spec(string name, KafkaOptions options, TimeSpan retention)
    {
        var configs = new Dictionary<string, string>
        {
            ["retention.ms"] = ((long)retention.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
        };
        // With acks=all, a write needs this many copies; one less than the replicas lets a broker go down
        if (options.ReplicationFactor >= 3)
        {
            configs["min.insync.replicas"] = (options.ReplicationFactor - 1).ToString(CultureInfo.InvariantCulture);
        }
        return new TopicSpecification
        {
            Name = name,
            NumPartitions = options.Partitions,
            ReplicationFactor = options.ReplicationFactor,
            Configs = configs,
        };
    }
}
