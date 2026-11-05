using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Safety;

// What a post or comment must pass before it's saved: quick rules first (blocked terms, link spam, flooding), then
// the offensive-language classifier for what rules can't see. Like photo checks it fails closed: if the classifier
// can't run, this throws and nothing is saved.
public partial class TextModerator(IOptions<ModerationOptions> options, ToxicityClassifier toxicity)
{
    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(.)\1+")]
    private static partial Regex RepeatPattern();

    private Regex? blocked;
    private int blockedFor;

    // Null when the text is fine; otherwise which rule it broke, with the classifier's score (1 for rules)
    public async Task<(string Category, double Score)?> CheckAsync(string text, CancellationToken cancellationToken)
    {
        if (CheckRules(text) is { } rule)
        {
            return (rule, 1);
        }

        var scores = await toxicity.ScoreAsync(text, cancellationToken);
        var worst = options.Value.ToxicityRejectAt
            .Where(limit => scores.GetValueOrDefault(limit.Key) >= limit.Value)
            .OrderByDescending(limit => scores[limit.Key] - limit.Value)
            .Select(limit => (ModerationCategories.ForToxicityLabel(limit.Key), Math.Round(scores[limit.Key], 3)))
            .FirstOrDefault();
        return worst.Item1 is null ? null : worst;
    }

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
