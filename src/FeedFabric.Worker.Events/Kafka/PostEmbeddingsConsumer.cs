using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Worker.Events.Embeddings;

namespace FeedFabric.Worker.Events.Kafka;

// Reads the posts topic in its own group, so embedding never holds up feed fan-out
public class PostEmbeddingsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    IProducer<string, string> producer,
    TimeProvider timeProvider,
    ILogger<PostEmbeddingsConsumer> logger) : EventConsumer(scopeFactory, options.Value, producer, timeProvider, logger)
{
    private bool backfilled;

    protected override string Topic => Topics.Posts;

    protected override string GroupId => options.Value.EmbeddingsGroupId;

    protected override async Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handler = services.GetRequiredService<PostEmbeddingHandler>();
        if (!backfilled)
        {
            backfilled = true;
            await handler.BackfillAsync(cancellationToken);
        }

        if (type is EventTypes.PostCreated or EventTypes.PostUpdated)
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<PostCreatedEvent>>(payload, EventJson.Options)
                ?? throw new JsonException("Payload is null.");
            await handler.HandleAsync(envelope, cancellationToken);
        }
    }
}
