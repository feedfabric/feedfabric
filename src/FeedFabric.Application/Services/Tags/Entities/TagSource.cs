namespace FeedFabric.Application.Services.Tags;

public enum TagSource
{
    // The author wrote it as a #hashtag
    Hashtag,

    // Matched from the post's wording by TopicRules
    Inferred,

    // Set by the host's app in the post's "tags", not written in the text
    App,
}

public static class TagSourceNames
{
    public const string Hashtag = "hashtag";
    public const string Inferred = "inferred";
    public const string App = "app";
}
