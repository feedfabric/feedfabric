using System.Reflection;
using System.Text.Json;

namespace FeedFabric.Simulator;

// The British English conversation library from ml/generate_conversations.py (Data/conversations.json): threads of a
// post and its comments per persona, plus the lines they're made of, for live comments that fit the post.
// All synthetic: none of it is real members' words.
public sealed class Conversations
{
    private const string ResourceName = "FeedFabric.Simulator.Data.conversations.json";
    private static readonly Lazy<Conversations> Instance = new(Load);

    public static Conversations Library => Instance.Value;

    public required IReadOnlyList<Thread> Threads { get; init; }

    public required IReadOnlyDictionary<string, Thread[]> ByPersona { get; init; }

    // Comment lines by kind of thread and who writes them, for live comments
    private Dictionary<string, string[]> advice = [];
    private string[] congratulations = [];
    private string[] thanks = [];
    private string[] agreement = [];

    // A comment that fits the post: advice for a question (in the author's persona's topic), congratulations for
    // anything else; or, replying to a comment, thanks from the poster or agreement from anyone else
    public string CommentFor(string postContent, string? authorPersona, bool replyFromPoster, bool isReply, Random random)
    {
        if (isReply)
        {
            return replyFromPoster ? Pick(thanks, random) : Pick(agreement, random);
        }
        return postContent.Contains('?') && authorPersona is not null && advice.TryGetValue(authorPersona, out var lines)
            ? Pick(lines, random)
            : Pick(congratulations, random);
    }

    private static string Pick(string[] lines, Random random) => lines[random.Next(lines.Length)];

    private static Conversations Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}");
        var file = JsonSerializer.Deserialize<File>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The conversation library is empty.");

        string[] Lines(Func<Thread, bool> thread, Func<Reply, bool> reply) =>
            file.Threads.Where(thread).SelectMany(t => t.Replies.Where(reply)).Select(r => r.Text).Distinct().ToArray();

        return new Conversations
        {
            Threads = file.Threads,
            ByPersona = file.Threads.GroupBy(t => t.Persona).ToDictionary(g => g.Key, g => g.ToArray()),
            advice = file.Threads.Where(t => t.Kind == "question").GroupBy(t => t.Persona)
                .ToDictionary(g => g.Key, g => g.SelectMany(t => t.Replies.Where(r => r.Who == "helper" && r.Parent is null)).Select(r => r.Text).Distinct().ToArray()),
            congratulations = Lines(t => t.Kind == "milestone", r => r.Who == "peer"),
            thanks = Lines(t => true, r => r.Who == "op" && r.Parent is not null && !r.Text.EndsWith('?')),
            agreement = Lines(t => t.Kind == "discussion", r => r.Parent is not null),
        };
    }

    private sealed record File(string Locale, bool Synthetic, Thread[] Threads);

    public sealed record Thread(string Persona, string Kind, string Post, Reply[] Replies);

    // Who writes it: "op" (the poster), "helper" (someone on the same topic) or "peer" (anyone); Parent is the index
    // of the comment it replies to
    public sealed record Reply(string Who, string Text, int? Parent);
}
