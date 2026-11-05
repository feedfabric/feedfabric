namespace FeedFabric.Application.Services.Media;

// Data for EventTypes.MediaUploaded: the client says the upload is done, so the worker can check it
public class MediaUploadedEvent
{
    public required Guid MediaId { get; set; }

    public required Guid OwnerId { get; set; }
}
