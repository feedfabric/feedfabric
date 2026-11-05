using System.Reflection;

namespace FeedFabric.Simulator;

// Health posts from Data/posts.tsv (persona key, tab, text), grouped by persona
public static class PostContent
{
    private const string ResourceName = "FeedFabric.Simulator.Data.posts.tsv";

    private static readonly Lazy<Dictionary<string, string[]>> Posts = new(Load);

    public static string Next(Persona persona, Random random)
    {
        var posts = Posts.Value[persona.Key];
        return posts[random.Next(posts.Length)];
    }

    private static Dictionary<string, string[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}");
        using var reader = new StreamReader(stream);
        var lines = new List<(string Key, string Text)>();
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            var tab = line.IndexOf('\t');
            lines.Add((line[..tab], line[(tab + 1)..]));
        }
        return lines.GroupBy(l => l.Key).ToDictionary(g => g.Key, g => g.Select(l => l.Text).ToArray());
    }
}
