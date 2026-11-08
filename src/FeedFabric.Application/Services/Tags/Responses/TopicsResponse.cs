namespace FeedFabric.Application.Services.Tags;

public class TopicsResponse
{
    // "saved" (chosen in the admin panel), "configuration" (Topics:Keywords), "default" (the built-in wellness set)
    // or "off" (Topics:Enabled is false)
    public required string Source { get; init; }

    // The saved version; 0 when the topics aren't from the database
    public int Version { get; init; }

    public string? Library { get; init; }

    public DateTime? SavedAt { get; init; }

    public string? SavedBy { get; init; }

    public required IReadOnlyDictionary<string, List<string>> Topics { get; init; }

    public static TopicsResponse From(CurrentTopics current) => new()
    {
        Source = current.Source.ToString().ToLowerInvariant(),
        Version = current.Version,
        Library = current.Library,
        SavedAt = current.SavedAt,
        SavedBy = current.SavedBy,
        Topics = current.Topics,
    };
}

public sealed record TopicMatchResponse(string Topic, double Confidence);
