using System.Text.RegularExpressions;

namespace FeedFabric.Application.Services.Tags;

// Keyword rules that tag a post with topics from a fixed list. A first pass: swap for an embedding
// classifier once there's labelled data, keeping the same topic names.
public static class TopicRules
{
    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["running"] = ["run", "ran", "running", "jog", "5k", "10k", "parkrun", "marathon", "/km", "pace", "intervals", "tempo"],
        ["walking"] = ["walk", "walked", "walking", "stroll", "steps", "hike", "hiked", "hiking", "pram walk"],
        ["travel"] = ["holiday", "trip", "travel", "back from", "beach", "villages", "balcony", "waterfall", "lisbon", "crete", "lake district",
            "cornwall", "lanzarote", "tuscany", "sri lanka", "edinburgh", "algarve", "bali", "iceland", "mallorca", "peak district", "kyoto",
            "snowdonia", "barcelona", "dolomites", "norfolk", "costa rica", "santorini", "vienna", "dorset"],
        ["yoga"] = ["yoga", "pilates", "flow", "pose", "savasana", "sun salutations", "stretch", "stretching"],
        ["strength"] = ["gym", "squats", "deadlifts", "bench", "overhead press", "pull-ups", "push day", "leg day", "kettlebell", "rep", "lifting",
            "strength", "protein"],
        ["cycling"] = ["bike", "ride", "rode", "cycle", "cycled", "cycling", "saddle", "hill ride"],
        ["swimming"] = ["swim", "swam", "swimming", "lido", "open water", "sea swim", "snorkelled", "cold water"],
        ["sleep"] = ["sleep", "slept", "bedtime", "early night", "hours of sleep", "nap"],
        ["nutrition"] = ["coffee", "drinking more water", "meal prep", "cooking", "breakfast", "fruit", "snack", "alcohol", "herbal tea"],
        ["mindfulness"] = ["meditation", "breathwork", "journaling", "body scan", "no screens", "phone-free", "switched off", "calm"],
        ["recovery"] = ["physio", "rehab", "injury", "sore", "my knee", "my lower back", "my ankle", "my hips", "rest day", "recovery"],
        ["parenting"] = ["baby", "kids", "pram", "buggy", "nap times", "nap time"],
        ["workday"] = ["desk", "meeting", "lunch break", "taking the stairs", "took the stairs", "3pm", "standing"],
        ["beginner"] = ["first ever", "couch to 5k", "beginner", "i'm new", "bought proper trainers", "scared of the gym"],
    };

    // Word boundaries, so "ran" doesn't match "orange" and "rep" doesn't match "prep"
    private static readonly Dictionary<string, Regex> Patterns = Keywords.ToDictionary(
        k => k.Key,
        k => new Regex(
            string.Join('|', k.Value.Select(w => $@"(?<![\w]){Regex.Escape(w)}(?![\w])")),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

    public static IReadOnlyCollection<string> Names => Keywords.Keys;

    // One matching keyword is a hint (0.5); each extra match adds 0.25, up to 1
    public static IEnumerable<(string Topic, double Confidence)> Match(string content)
    {
        foreach (var (topic, pattern) in Patterns)
        {
            var matches = pattern.Matches(content).Select(m => m.Value.ToLowerInvariant()).Distinct().Count();
            if (matches > 0)
            {
                yield return (topic, Math.Min(1, 0.5 + 0.25 * (matches - 1)));
            }
        }
    }
}
