using System.Text.Json;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Worker.Engagement;

namespace FeedFabric.Worker.Events.Kafka;

// Reads the engagement topic. Its own group, so engagement can lag or fail without holding up feed fan-out.
public class EngagementEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    TimeProvider timeProvider,
    ILogger<EngagementEventsConsumer> logger) : EventConsumer(scopeFactory, options.Value.BootstrapServers, timeProvider, logger)
{
    protected override string Topic => Topics.Engagement;

    protected override string GroupId => options.Value.EngagementGroupId;

    protected override async Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case EventTypes.PostLiked or EventTypes.PostUnliked:
                var liked = JsonSerializer.Deserialize<EventEnvelope<PostLikedEvent>>(payload, EventJson.Options)
                    ?? throw new JsonException("Payload is null.");
                await services.GetRequiredService<LikeHandler>().HandleAsync(liked, cancellationToken);
                break;
            case EventTypes.CommentCreated:
                var commented = JsonSerializer.Deserialize<EventEnvelope<CommentCreatedEvent>>(payload, EventJson.Options)
                    ?? throw new JsonException("Payload is null.");
                await services.GetRequiredService<CommentHandler>().HandleAsync(commented, cancellationToken);
                break;
            default:
                // Event types this worker doesn't handle yet
                break;
        }
    }
}
