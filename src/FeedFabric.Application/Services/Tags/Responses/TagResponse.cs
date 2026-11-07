namespace FeedFabric.Application.Services.Tags;

public class TagResponse
{
    public required string Tag { get; init; }

    // "hashtag" or "inferred"
    public required string Source { get; init; }
}

public class TrendingTagResponse
{
    public required string Tag { get; set; }

    public int Posts { get; init; }
}
