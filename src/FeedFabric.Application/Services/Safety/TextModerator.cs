using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Safety;

// What a post or comment must pass before it's saved: quick rules first (blocked terms, link spam, flooding), then
// the offensive-language classifier for what rules can't see. Rule breaks and threats are blocked; what the
// classifier is less sure is harm (insults aimed at someone, hate, sexually explicit) is held for a reviewer instead,
// so a false positive costs a short wait rather than a post. Like photo checks it fails closed: if the classifier can't run,
// this throws and nothing is saved.
public partial class TextModerator(IOptions<ModerationOptions> options, ToxicityClassifier toxicity)
{
    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(.)\1+")]
    private static partial Regex RepeatPattern();

    // Second person, including the usual shorthand and number-for-letter spellings: you, your, you're, u, ur, y0u, ya
    [GeneratedRegex(@"(?<![\w@])(?:y[o0]u(?:r|rs|re|'re|’re|ve|'ve|ll|'ll|'d|rself|rselves)?|u|ur|ya|yer)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex SecondPersonPattern();

    private Regex? blocked;
    private int blockedFor;

    // Null when the text is fine; otherwise what to do and which rule it broke, with the classifier's score (1 for
    // rules). Blocking wins over holding.
    public async Task<ModerationVerdict?> CheckAsync(string text, CancellationToken cancellationToken)
    {
        if (CheckRules(text) is { } rule)
        {
            return new ModerationVerdict(ModerationAction.Block, rule, 1);
        }

        var settings = options.Value;
        var scores = await toxicity.ScoreAsync(text, cancellationToken);
        if (!AddressesSomeone(text))
        {
            // Labels that only count when the text speaks to someone don't count here
            scores = scores.Where(s => !settings.ToxicityOnlyWhenAddressed.Contains(s.Key)).ToDictionary();
        }
        return Worst(ModerationAction.Block, settings.ToxicityRejectAt, scores)
            ?? Worst(ModerationAction.Hold, settings.ToxicityHoldAt, scores);
    }

    // Whether the text speaks to someone: "you're an idiot" does, "grinning like an idiot" doesn't
    public static bool AddressesSomeone(string text) => SecondPersonPattern().IsMatch(text);

    // The label furthest past its limit
    private static ModerationVerdict? Worst(ModerationAction action, Dictionary<string, double> limits, IReadOnlyDictionary<string, double> scores) =>
        limits
            .Where(limit => scores.GetValueOrDefault(limit.Key) >= limit.Value)
            .OrderByDescending(limit => scores[limit.Key] - limit.Value)
            .Select(limit => new ModerationVerdict(action, ModerationCategories.ForToxicityLabel(limit.Key), Math.Round(scores[limit.Key], 3)))
            .FirstOrDefault();

    // The rules alone, without the classifier
    public string? CheckRules(string text)
    {
        var settings = options.Value;
        if (Blocked(settings.BlockedTerms)?.IsMatch(text) == true)
        {
            return ModerationCategories.BlockedTerm;
        }
        if (LinkPattern().Matches(text).Count > settings.MaxLinks)
        {
            return ModerationCategories.LinkSpam;
        }
        if (RepeatPattern().Matches(text).Any(m => m.Length >= settings.MaxRepeatedCharacters))
        {
            return ModerationCategories.Flooding;
        }
        return null;
    }

    // Whole words or phrases, ignoring case; rebuilt only when the list changes
    private Regex? Blocked(List<string> terms)
    {
        var hash = string.Join('\n', terms).GetHashCode();
        if (terms.Count == 0)
        {
            return null;
        }
        if (blocked is null || blockedFor != hash)
        {
            var pattern = string.Join('|', terms.Where(t => t.Trim().Length > 0).Select(t => $@"(?<!\w){Regex.Escape(t.Trim())}(?!\w)"));
            blocked = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            blockedFor = hash;
        }
        return blocked;
    }
}

public enum ModerationAction
{
    // Refuse it: nothing is saved
    Block,

    // Save it, visible only to its author until a reviewer decides
    Hold,
}

public record ModerationVerdict(ModerationAction Action, string Category, double Score);
