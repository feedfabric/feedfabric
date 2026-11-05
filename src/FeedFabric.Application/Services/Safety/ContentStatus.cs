namespace FeedFabric.Application.Services.Safety;

// Who can see a post or comment. Held ones are saved and fan out like any other, but only their author sees them
// until a reviewer decides; feeds filter on read, so an approved post appears where it would have been all along.
public enum ContentStatus
{
    Visible,
    // The classifier wasn't sure enough to block it, or enough people reported it: waiting for a reviewer
    Held,
    // A reviewer took it down. Nobody sees it, the author included.
    Removed,
}
