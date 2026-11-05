namespace FeedFabric.Worker.Events.Kafka;

public class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    // Every instance of this worker shares the group, so Kafka splits the partitions between them
    public string GroupId { get; set; } = "worker-events";

    // Separate group for the engagement topic, so it keeps its own offsets and pace
    public string EngagementGroupId { get; set; } = "worker-engagement";

    // Separate group for the media topic: scans are slow and mustn't hold up anything else
    public string MediaGroupId { get; set; } = "worker-media";

    // Separate group for embedding posts: a nice-to-have that must never slow fan-out
    public string EmbeddingsGroupId { get; set; } = "worker-embeddings";

    public int Partitions { get; set; } = 3;
}
