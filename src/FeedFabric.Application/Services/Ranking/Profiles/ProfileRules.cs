using FeedFabric.Application.Services.Feed;

namespace FeedFabric.Application.Services.Ranking.Profiles;

// How a profile's filters, boosts and formula inputs apply to one post, kept apart from ForYouService so they can be
// tested on their own
public static class ProfileRules
{
    private static readonly int HasMedia = RankingFeatures.Index("has_media");
    private static readonly int Likes = RankingFeatures.Index("likes");
    private static readonly int Follows = RankingFeatures.Index("follows_author");

    // A multiplier that also works on negative scores (the model's value goes below 0 when a skip is likely):
    // above 1 always lifts a post, below 1 always lowers it, and 0 sinks it to the bottom
    public static double Multiply(double score, double multiplier) =>
        multiplier <= 0 ? -1e6 + score
        : score >= 0 ? score * multiplier
        : score / multiplier;

    public static bool Passes(HomeFeedPostResponse post, double[] row, FilterSettings filters)
    {
        if (filters.MediaOnly && row[HasMedia] == 0 || row[Likes] < filters.MinLikes)
        {
            return false;
        }
        if (filters.ExcludeTags.Count > 0 && post.Tags.Any(t => filters.ExcludeTags.Contains(t.Tag)))
        {
            return false;
        }
        return filters.OnlyTags.Count == 0 || post.Tags.Any(t => filters.OnlyTags.Contains(t.Tag));
    }

    public static bool Matches(BoostRule boost, HomeFeedPostResponse post, double[] row) => boost.Match switch
    {
        BoostMatch.Tag => post.Tags.Any(t => t.Tag == boost.Value),
        BoostMatch.Author => post.AuthorUsername == boost.Value,
        BoostMatch.HasMedia => row[HasMedia] > 0,
        BoostMatch.Following => row[Follows] > 0,
        BoostMatch.NotFollowing => row[Follows] == 0,
        _ => false,
    };

    public static string Describe(BoostRule boost) => boost.Match switch
    {
        BoostMatch.Tag => $"Tagged #{boost.Value}",
        BoostMatch.Author => $"By @{boost.Value}",
        BoostMatch.HasMedia => "Has photos",
        BoostMatch.Following => "From someone you follow",
        BoostMatch.NotFollowing => "From someone you don't follow",
        _ => "",
    } + $" (×{boost.Multiplier:0.##})";

    // The model's value as a formula's "model": the weighted sum of its predictions, plus the most the negative goals
    // could take away, so it's 0 or more and keeps the same order. A formula can then multiply it by a decay or a
    // boost without that lifting posts the model expects to be skipped (a negative value times 0.5 would go up).
    public static double[] NonNegativeModel(double[] values, Goals goals)
    {
        var floor = goals.ToDictionary().Values.Where(w => w < 0).Sum();
        return values.Select(v => Math.Max(0, v - floor)).ToArray();
    }
}
