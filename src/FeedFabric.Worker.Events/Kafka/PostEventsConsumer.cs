using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Users;
using FeedFabric.Worker.Events.Accounts;
using FeedFabric.Worker.Events.FanOut;

namespace FeedFabric.Worker.Events.Kafka;

// Reads the posts topic and hands each event to its handlers
public class PostEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    IProducer<string, string> producer,
    TimeProvider timeProvider,
    ILogger<PostEventsConsumer> logger) : EventConsumer(scopeFactory, options.Value, producer, timeProvider, logger)
{
    protected override string Topic => Topics.Posts;

    protected override string GroupId => options.Value.GroupId;

    protected override async Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case EventTypes.PostCreated:
                var postCreated = JsonSerializer.Deserialize<EventEnvelope<PostCreatedEvent>>(payload, EventJson.Options)
                    ?? throw new JsonException("Payload is null.");
                await services.GetRequiredService<FeedFanOutHandler>().HandleAsync(postCreated, cancellationToken);
                break;
            case EventTypes.UserDeleted:
                var userDeleted = JsonSerializer.Deserialize<EventEnvelope<UserDeletedEvent>>(payload, EventJson.Options)
                    ?? throw new JsonException("Payload is null.");
                await services.GetRequiredService<AccountErasureHandler>().HandleAsync(userDeleted, cancellationToken);
                break;
            default:
                // Event types this worker doesn't handle yet
                break;
        }
    }
}
