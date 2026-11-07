using Confluent.Kafka;

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

    // For topics this worker creates. Partitions cap how many instances share a topic's work; they can be raised
    // later (kafka-topics --alter) but never lowered, and raising them moves keys between partitions.
    public int Partitions { get; set; } = 3;

    // Copies of each partition. 3 survives a broker loss; a single dev broker needs 1 (appsettings.Development.json).
    public short ReplicationFactor { get; set; } = 3;

    // How long the event topics keep events. Postgres is the source of truth, so this only bounds replay.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    // Dead-lettered events wait for a person, so they're kept longer
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(28);

    // Tries before an event that keeps failing is dead-lettered. Failures because a dependency is down don't count.
    public int MaxHandlerAttempts { get; set; } = 5;

    public KafkaSecurityOptions Security { get; set; } = new();
}

// How the worker connects to the brokers. Plaintext is for a local broker only; managed Kafka (Confluent Cloud, MSK,
// Aiven) needs SaslSsl with a username and password from the secret store.
public class KafkaSecurityOptions
{
    // Plaintext, Ssl, SaslPlaintext or SaslSsl
    public SecurityProtocol Protocol { get; set; } = SecurityProtocol.Plaintext;

    // Plain, ScramSha256 or ScramSha512
    public SaslMechanism Mechanism { get; set; } = SaslMechanism.ScramSha512;

    public string? Username { get; set; }

    public string? Password { get; set; }

    // A PEM file of CA certificates, when the brokers' certificate isn't signed by a public CA
    public string? CaLocation { get; set; }

    public void ApplyTo(ClientConfig config)
    {
        config.SecurityProtocol = Protocol;
        if (Protocol is SecurityProtocol.SaslSsl or SecurityProtocol.SaslPlaintext)
        {
            config.SaslMechanism = Mechanism;
            config.SaslUsername = Username;
            config.SaslPassword = Password;
        }
        if (!string.IsNullOrEmpty(CaLocation))
        {
            config.SslCaLocation = CaLocation;
        }
    }
}
