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

    public static IReadOnlyList<string> Hashtags(string content) =>
        HashtagPattern().Matches(content).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    public static IReadOnlyList<string> Mentions(string content) =>
        MentionPattern().Matches(content).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    // Hashtags win over inferred topics with the same name
    public static List<PostTagEntity> TagsFor(Guid postId, string content, DateTime createdAt)
    {
        var tags = Hashtags(content).ToDictionary(
            tag => tag,
            tag => new PostTagEntity { PostId = postId, Tag = tag, Source = TagSource.Hashtag, Confidence = 1, CreatedAt = createdAt });
        foreach (var (topic, confidence) in TopicRules.Match(content))
        {
            tags.TryAdd(topic, new PostTagEntity { PostId = postId, Tag = topic, Source = TagSource.Inferred, Confidence = confidence, CreatedAt = createdAt });
        }
        return tags.Values.ToList();
    }
}
