using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Feed;

public class HomeFeedResponse
{
    public required IReadOnlyList<HomeFeedPostResponse> Posts {get;init;}

    // Pass back as ?cursor= for the next, older page; null when there are no more posts
    public Guid? NextCursor {get;set;}

    // Where this page came from: "redis", "postgres", or "redis+postgres" when a page ran past the cached timeline
    public required string Source {get;init;}
}

public class HomeFeedPostResponse
{
    public required Guid Id {get;init;}

    public required Guid AuthorId {get;init;}

    public required string AuthorExternalUserId {get;init;}

    public required string AuthorUsername {get;init;}

    public required string Content {get;init;}

    public DateTime CreatedAt {get;init;}

    public DateTime? EditedAt {get;set;}

    // On a repost: the post it shares, in RepostOf. The repost itself has no content, tags or media; its author
    // is who reposted it and CreatedAt is when.
    public Guid? RepostOfId {get;init;}

    public HomeFeedPostResponse? RepostOf {get;set;}

    // Held for review: only its author is ever shown it, so they know why nobody else can see it yet
    public bool IsHeld {get;set;}

    // Hashtags first, then inferred topics
    public IReadOnlyList<TagResponse> Tags {get;init;} = [];

    // Scanned, rebuilt images with short-lived URLs
    public IReadOnlyList<PostMediaResponse> Media {get;set;} = [];

    // Only in the For You feed: why the post is where it is
    public PostRanking? Ranking {get;set;}
}
