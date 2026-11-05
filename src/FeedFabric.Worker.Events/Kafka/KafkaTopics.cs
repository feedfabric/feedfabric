using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FeedFabric.Application.Services.Messaging;

namespace FeedFabric.Worker.Events.Kafka;

public static class KafkaTopics
{
    // Creates the topics this worker uses if they don't exist yet, so the consumer never subscribes to a missing topic
    public static async Task EnsureCreatedAsync(KafkaOptions options)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([
                new TopicSpecification { Name = Topics.Posts, NumPartitions = options.Partitions, ReplicationFactor = 1 },
                new TopicSpecification { Name = Topics.Engagement, NumPartitions = options.Partitions, ReplicationFactor = 1 },
                new TopicSpecification { Name = Topics.Media, NumPartitions = options.Partitions, ReplicationFactor = 1 },
            ]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
        }
    }
}
