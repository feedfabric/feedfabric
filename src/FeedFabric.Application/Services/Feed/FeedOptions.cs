namespace FeedFabric.Application.Services.Feed;

public class FeedOptions
{
    public const string Section = "Feed";

    // Newest entries kept per timeline; older posts can be loaded from Postgres
    public int MaxLength { get; set; } = 500;
}
