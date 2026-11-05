namespace FeedFabric.Application.Services.Tags;

public class TagResponse
{
    public required string Tag { get; set; }

    // "hashtag" or "inferred"
    public required string Source { get; set; }
}

public class TrendingTagResponse
{
    public required string Tag { get; set; }

    public int Posts { get; set; }
}
