namespace FeedFabric.Application.Services.Tags;

// One tag on one post; the pair is the key. A hashtag that is also an inferred topic is stored once, as the hashtag.
public class PostTagEntity
{
    public required Guid PostId { get; init; }

    // Lowercase, without the #
    public required string Tag { get; init; }

    public TagSource Source { get; init; }

    // 1 for hashtags; for inferred topics, how strongly the wording matched
    public double Confidence { get; init; }

    // The post's CreatedAt, so trending can count recent tags without joining posts
    public DateTime CreatedAt { get; init; }
}
