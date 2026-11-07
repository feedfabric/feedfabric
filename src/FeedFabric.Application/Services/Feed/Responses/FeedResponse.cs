using FeedFabric.Application.Services.Posts;

namespace FeedFabric.Application.Services.Feed;

public class FeedResponse
{
    public required IReadOnlyList<PostResponse> Posts {get;set;}

    // Pass back as ?cursor= for the next, older page; null when there are no more posts
    public Guid? NextCursor {get;set;}
}
