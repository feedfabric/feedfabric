namespace FeedFabric.Application.Services.Tags;

// One saved version of the community's topics. Saving adds a row rather than changing one, so earlier sets stay
// readable and can be restored; the newest is the one in use.
public class TopicSetEntity : EntityBase
{
    public int Version { get; init; }

    // Topic name to keywords, as JSON
    public required string Topics { get; init; }

    // The library set it started from (TopicLibrary), if any
    public string? Library { get; init; }

    public DateTime SavedAt { get; init; }

    // The external user ID of whoever saved it
    public string? SavedBy { get; init; }
}
