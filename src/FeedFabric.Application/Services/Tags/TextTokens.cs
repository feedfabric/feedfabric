using System.Text.RegularExpressions;

namespace FeedFabric.Application.Services.Tags;

// #hashtags and @mentions in post and comment text. The web app links them with the same patterns.
public static partial class TextTokens
{
    // Starts with a letter so "#1" and "#5k" stay plain text
    [GeneratedRegex(@"(?<![\w#@])#([A-Za-z][A-Za-z0-9_]{1,49})")]
    private static partial Regex HashtagPattern();

    [GeneratedRegex(@"(?<![\w#@])@([A-Za-z0-9][A-Za-z0-9_-]{0,49})")]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{1,49}$")]
    private static partial Regex TagPattern();

    // A tag an app sets itself: what could follow a # in the text
    public static bool IsTag(string tag) => TagPattern().IsMatch(tag);

    public static IReadOnlyList<string> Hashtags(string content) =>
        [.. HashtagPattern().Matches(content).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct()];

    public static IReadOnlyList<string> Mentions(string content) =>
        [.. MentionPattern().Matches(content).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct()];

    // Hashtags win over inferred topics with the same name, and the app's own tags over both
    public static List<PostTagEntity> TagsFor(Guid postId, string content, DateTime createdAt, TopicMatcher topics,
        IEnumerable<string>? appTags = null)
    {
        var tags = (appTags ?? []).Select(tag => tag.ToLowerInvariant()).Distinct().ToDictionary(
            tag => tag,
            tag => new PostTagEntity { PostId = postId, Tag = tag, Source = TagSource.App, Confidence = 1, CreatedAt = createdAt });
        foreach (var tag in Hashtags(content))
        {
            tags.TryAdd(tag, new PostTagEntity { PostId = postId, Tag = tag, Source = TagSource.Hashtag, Confidence = 1, CreatedAt = createdAt });
        }
        foreach (var (topic, confidence) in topics.Match(content))
        {
            tags.TryAdd(topic, new PostTagEntity { PostId = postId, Tag = topic, Source = TagSource.Inferred, Confidence = confidence, CreatedAt = createdAt });
        }
        return [.. tags.Values];
    }
}
