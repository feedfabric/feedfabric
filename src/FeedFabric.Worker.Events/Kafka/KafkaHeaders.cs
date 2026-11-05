namespace FeedFabric.Worker.Events.Kafka;

public static class KafkaHeaders
{
    // The event type, so consumers can pick a handler without parsing the payload
    public const string Type = "type";
}
