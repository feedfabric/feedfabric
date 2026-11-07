using System.Text.RegularExpressions;

namespace FeedFabric.Application.Services.Tags;

// What's wrong with one topic in a set; Topic is empty for the set as a whole
public sealed record TopicError(string Topic, string Message);

// Keyword rules for one set of topics, compiled. A first pass: swap for an embedding classifier once there's labelled
// data, keeping the same topic names.
public sealed partial class TopicMatcher
{
    public const int MaxTopics = 50;
    public const int MaxKeywords = 100;
    public const int MaxKeywordLength = 60;

    private readonly Dictionary<string, Regex> patterns;

    // Expects a set that's been through Normalise
    public TopicMatcher(IReadOnlyDictionary<string, List<string>> topics)
    {
        // Word boundaries, so "ran" doesn't match "orange" and "rep" doesn't match "prep"
        patterns = topics.Where(t => t.Value.Count > 0).ToDictionary(
            t => t.Key,
            t => new Regex(
                string.Join('|', t.Value.Select(w => $@"(?<![\w]){Regex.Escape(w)}(?![\w])")),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
    }

    public IReadOnlyCollection<string> Names => patterns.Keys;

    // One matching keyword is a hint (0.5); each extra match adds 0.25, up to 1
    public IEnumerable<(string Topic, double Confidence)> Match(string content)
    {
        foreach (var (topic, pattern) in patterns)
        {
            var matches = pattern.Matches(content).Select(m => m.Value.ToLowerInvariant()).Distinct().Count();
            if (matches > 0)
            {
                yield return (topic, Math.Min(1, 0.5 + 0.25 * (matches - 1)));
            }
        }
    }

    // Lowercase names, as tags are stored; keywords trimmed and lowercased, blanks and repeats dropped
    public static Dictionary<string, List<string>> Normalise(IReadOnlyDictionary<string, List<string>> topics) =>
        topics.GroupBy(t => t.Key.Trim().ToLowerInvariant()).ToDictionary(
            g => g.Key,
            g => g.SelectMany(t => t.Value).Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList());

    public static Dictionary<string, List<string>> From(TopicLibrarySet set) =>
        set.Keywords.ToDictionary(t => t.Key, t => t.Value.ToList());

    // Everything wrong with a set, so it can be refused with every problem listed. An empty set is allowed: posts are
    // tagged with their hashtags only.
    public static List<TopicError> Problems(IReadOnlyDictionary<string, List<string>> topics)
    {
        var problems = new List<TopicError>();
        if (topics.Count > MaxTopics)
        {
            problems.Add(new TopicError("", $"Use at most {MaxTopics} topics."));
        }
        foreach (var group in topics.GroupBy(t => t.Key.Trim().ToLowerInvariant()))
        {
            var name = group.First().Key;
            if (!NamePattern().IsMatch(group.Key))
            {
                problems.Add(new TopicError(name, "A topic name is a tag, so a letter then 1 to 49 letters, digits or _."));
            }
            if (group.Count() > 1)
            {
                problems.Add(new TopicError(name, "Another topic has the same name."));
            }
            var keywords = group.SelectMany(t => t.Value).Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            switch (keywords.Count)
            {
                case 0:
                    problems.Add(new TopicError(name, "Give it at least one keyword."));
                    break;
                case > MaxKeywords:
                    problems.Add(new TopicError(name, $"Use at most {MaxKeywords} keywords."));
                    break;
            }
            if (keywords.FirstOrDefault(k => k.Length > MaxKeywordLength) is { } tooLong)
            {
                problems.Add(new TopicError(name, $"\"{tooLong[..20]}…\" is longer than {MaxKeywordLength} characters."));
            }
        }
        return problems;
    }

    // The same as a hashtag (TextTokens), so a topic and a hashtag with one name are one tag
    [GeneratedRegex("^[a-z][a-z0-9_]{1,49}$")]
    private static partial Regex NamePattern();
}
