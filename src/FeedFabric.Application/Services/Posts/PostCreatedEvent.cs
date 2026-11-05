namespace FeedFabric.Application.Services.Posts;

// Data for EventTypes.PostCreated and EventTypes.PostUpdated. Handlers that need the content read the post, so edits are never stale.
public class PostCreatedEvent
{
    public required Guid PostId { get; set; }

    public required Guid AuthorId { get; set; }

    public DateTime CreatedAt { get; set; }
}
