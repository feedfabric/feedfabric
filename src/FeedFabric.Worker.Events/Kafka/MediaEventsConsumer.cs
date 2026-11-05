using System.Text.Json;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Worker.Events.Media;

namespace FeedFabric.Worker.Events.Kafka;

// Reads the media topic. Its own group, so slow scans never delay feed fan-out or engagement.
public class MediaEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaEventsConsumer> logger) : EventConsumer(scopeFactory, options.Value.BootstrapServers, timeProvider, logger)
{
    protected override string Topic => Topics.Media;

    protected override string GroupId => options.Value.MediaGroupId;

    protected override async Task HandleAsync(string? type, string payload, IServiceProvider services, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case EventTypes.MediaUploaded:
                var uploaded = JsonSerializer.Deserialize<EventEnvelope<MediaUploadedEvent>>(payload, EventJson.Options)
                    ?? throw new JsonException("Payload is null.");
                await services.GetRequiredService<MediaProcessor>().HandleAsync(uploaded, cancellationToken);
                break;
            default:
                // Event types this worker doesn't handle yet
                break;
        }
    }
}
