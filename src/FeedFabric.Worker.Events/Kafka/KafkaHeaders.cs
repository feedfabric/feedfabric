namespace FeedFabric.Worker.Events.Kafka;

public static class KafkaHeaders
{
    // The event type, so consumers can pick a handler without parsing the payload
    public const string Type = "type";

    // W3C trace context of the request that wrote the event, so its trace carries on in the worker
    public const string TraceParent = "traceparent";

    public const string TraceState = "tracestate";

    // Added to a dead-lettered event: where it was read from, by which group, how many tries and the last error
    public const string DeadLetterTopic = "dead-letter.topic";
    public const string DeadLetterPartition = "dead-letter.partition";
    public const string DeadLetterOffset = "dead-letter.offset";
    public const string DeadLetterGroup = "dead-letter.group";
    public const string DeadLetterAttempts = "dead-letter.attempts";
    public const string DeadLetterError = "dead-letter.error";
}
