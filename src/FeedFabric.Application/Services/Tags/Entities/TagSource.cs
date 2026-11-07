namespace FeedFabric.Application.Services.Tags;

public enum TagSource
{
    // The author wrote it as a #hashtag
    Hashtag,

    // Matched from the post's wording by TopicRules
    Inferred,
}
