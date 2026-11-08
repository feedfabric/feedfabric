namespace FeedFabric.Application.Services.Tags;

public class TopicOptions
{
    public const string Section = "Topics";

    // Off tags posts with their hashtags only, whatever staff have chosen
    public bool Enabled { get; init; } = true;

    // Topic name to the words and phrases that suggest it, e.g. "bouldering": ["boulder", "crimp", "send"]. Names are
    // tags, so they follow the hashtag rules: a letter, then letters, digits or _. Used until staff choose topics in
    // the admin panel, which then take over. Left empty, the built-in wellness set (TopicLibrary) is used.
    public Dictionary<string, List<string>> Keywords { get; init; } = [];
}
