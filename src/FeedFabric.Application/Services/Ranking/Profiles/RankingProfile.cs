using System.Text.Json.Serialization;

namespace FeedFabric.Application.Services.Ranking.Profiles;

// How a For You feed is put together, as one named, versioned set of settings. Every part has a default that
// matches the feed before profiles existed (the "balanced" preset), so a profile only needs the parts it changes.
//   Sources     where candidates come from (as Stream's activity selectors)
//   Scoring     how they're scored: the trained model with your goals, the built-in formula, your own formula,
//               or newest first
//   Freshness   an extra fade with age, and a cut-off
//   Variety     the rules that keep one author or topic from taking over
//   Exploration slots for posts the ranker wouldn't have picked, so it keeps learning
//   Boosts      multiply the score of posts that match a rule
//   Filters     leave posts out altogether
public sealed class RankingProfile
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public SourceSettings Sources { get; set; } = new();

    public ScoringSettings Scoring { get; set; } = new();

    public FreshnessSettings Freshness { get; set; } = new();

    public VarietySettings Variety { get; set; } = new();

    public ExplorationSettings Exploration { get; set; } = new();

    public List<BoostRule> Boosts { get; set; } = [];

    public FilterSettings Filters { get; set; } = new();

    public const int MaxBoosts = 20;
    public const int MaxTags = 50;

    // Every problem, each with the camelCase path of the setting it's about
    public List<ProfileError> Validate()
    {
        var errors = new List<ProfileError>();
        void Range(string field, double value, double min, double max)
        {
            if (double.IsNaN(value) || value < min || value > max)
            {
                errors.Add(new ProfileError(field, $"Must be between {min} and {max}."));
            }
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60)
        {
            errors.Add(new ProfileError("name", "Give it a name of up to 60 characters."));
        }
        if (Description.Length > 300)
        {
            errors.Add(new ProfileError("description", "Keep the description to 300 characters."));
        }

        Range("sources.following.limit", Sources.Following.Limit, 0, 1000);
        Range("sources.topics.limit", Sources.Topics.Limit, 0, 500);
        Range("sources.similarReaders.limit", Sources.SimilarReaders.Limit, 0, 500);
        Range("sources.similarMeaning.limit", Sources.SimilarMeaning.Limit, 0, 500);
        Range("sources.topTopics", Sources.TopTopics, 1, 10);
        Range("sources.lookbackHours", Sources.LookbackHours, 1, 168);
        if (!Sources.Following.Enabled && !Sources.Topics.Enabled && !Sources.SimilarReaders.Enabled && !Sources.SimilarMeaning.Enabled)
        {
            errors.Add(new ProfileError("sources", "Turn on at least one source."));
        }

        foreach (var (goal, weight) in Scoring.Goals.ToDictionary())
        {
            Range($"scoring.goals.{goal}", weight, -10, 10);
        }
        if (Scoring.Mode == ScoringMode.Expression && !ScoreExpression.TryParse(Scoring.Expression ?? "", out _, out var error))
        {
            errors.Add(new ProfileError("scoring.expression", error!.Message, error.Position));
        }

        if (Freshness.HalfLifeHours is { } halfLife)
        {
            Range("freshness.halfLifeHours", halfLife, 0.25, 720);
        }
        if (Freshness.MaxAgeHours is { } maxAge)
        {
            Range("freshness.maxAgeHours", maxAge, 1, 2160);
        }

        Range("variety.authorCap", Variety.AuthorCap, 1, DiversityRules.Window);
        Range("variety.topicRun", Variety.TopicRun, 1, DiversityRules.Window);
        Range("variety.outOfNetwork", Variety.OutOfNetwork, 0, DiversityRules.Window);
        Range("variety.seenPenalty", Variety.SeenPenalty, 0, 1);

        Range("exploration.slots", Exploration.Slots, 0, 3);
        Range("exploration.fromRank", Exploration.FromRank, 10, 500);

        if (Boosts.Count > MaxBoosts)
        {
            errors.Add(new ProfileError("boosts", $"Use at most {MaxBoosts} boosts."));
        }
        for (var i = 0; i < Boosts.Count; i++)
        {
            var boost = Boosts[i];
            Range($"boosts[{i}].multiplier", boost.Multiplier, 0, 10);
            if (boost.Match is BoostMatch.Tag or BoostMatch.Author && string.IsNullOrWhiteSpace(boost.Value))
            {
                errors.Add(new ProfileError($"boosts[{i}].value", boost.Match == BoostMatch.Tag ? "Name the tag." : "Name the author's username."));
            }
        }

        if (Filters.ExcludeTags.Count > MaxTags || Filters.OnlyTags.Count > MaxTags)
        {
            errors.Add(new ProfileError("filters", $"Use at most {MaxTags} tags in each list."));
        }
        Range("filters.minLikes", Filters.MinLikes, 0, 10000);

        return errors;
    }

    // Tags and usernames compared the way they're stored: lowercase, no leading # or @
    public RankingProfile Normalised()
    {
        static string Clean(string value) => value.Trim().TrimStart('#', '@').ToLowerInvariant();
        foreach (var boost in Boosts)
        {
            boost.Value = boost.Value is null ? null : Clean(boost.Value);
        }
        Filters.ExcludeTags = Filters.ExcludeTags.Select(Clean).Where(t => t.Length > 0).Distinct().ToList();
        Filters.OnlyTags = Filters.OnlyTags.Select(Clean).Where(t => t.Length > 0).Distinct().ToList();
        Name = Name.Trim();
        Description = Description.Trim();
        Scoring.Expression = Scoring.Expression?.Trim();
        return this;
    }
}

public sealed class SourceSettings
{
    // 1. Posts from accounts the viewer follows (their home timeline)
    public SourceSetting Following { get; set; } = new() { Limit = 300 };

    // 2. What's doing well right now on the viewer's top topics
    public SourceSetting Topics { get; set; } = new();

    // 3. What people with the same taste liked lately
    public SourceSetting SimilarReaders { get; set; } = new();

    // 4. Posts close in meaning to what the viewer liked, from anyone
    public SourceSetting SimilarMeaning { get; set; } = new();

    // How many of the viewer's top topics Topics draws on
    public int TopTopics { get; set; } = 3;

    // How far back Topics and SimilarReaders look
    public double LookbackHours { get; set; } = 6;
}

public sealed class SourceSetting
{
    public bool Enabled { get; set; } = true;

    // The most candidates this source adds
    public int Limit { get; set; } = 100;
}

[JsonConverter(typeof(JsonStringEnumConverter<ScoringMode>))]
public enum ScoringMode
{
    // The trained model, its predictions weighted by Goals; the formula until a model is loaded
    [JsonStringEnumMemberName("model")]
    Model,
    // The built-in formula (HeuristicRanker)
    [JsonStringEnumMemberName("formula")]
    Formula,
    // Your own formula in Expression
    [JsonStringEnumMemberName("expression")]
    Expression,
    // Newest first
    [JsonStringEnumMemberName("newest")]
    Newest,
}

public sealed class ScoringSettings
{
    public ScoringMode Mode { get; set; } = ScoringMode.Model;

    // For Mode Expression: e.g. "model * decay_exp(age_hours, 12)"
    public string? Expression { get; set; }

    // What each of the model's predictions is worth when adding them up; negative for things to avoid
    public Goals Goals { get; set; } = new();
}

// The value of a post = Σ goal × chance of it happening, for the five things the model predicts
public sealed class Goals
{
    public double Open { get; set; } = 0.5;

    public double Like { get; set; } = 1;

    public double Comment { get; set; } = 3;

    // Stayed on it for 10 seconds or more
    public double LongRead { get; set; } = 2;

    public double Skip { get; set; } = -1.5;

    // Keyed by the model's head names
    public Dictionary<string, double> ToDictionary() => new()
    {
        ["open"] = Open, ["like"] = Like, ["comment"] = Comment, ["long_dwell"] = LongRead, ["skip"] = Skip,
    };
}

public sealed class FreshnessSettings
{
    // Halves a post's score every this many hours, on top of the ranker; null leaves age to the ranker
    public double? HalfLifeHours { get; set; }

    // Leaves out anything older; null for no limit
    public double? MaxAgeHours { get; set; }
}

public sealed class VarietySettings
{
    // At most this many posts by one author in any ten
    public int AuthorCap { get; set; } = 2;

    // At most this many posts on the same main topic in a row
    public int TopicRun { get; set; } = 2;

    // At least this many posts from accounts the viewer doesn't follow in each ten, when there are any
    public int OutOfNetwork { get; set; } = 2;

    // Posts seen in the last day keep this share of their score: 1 for no penalty, 0 to sink them
    public double SeenPenalty { get; set; } = 0.35;
}

public sealed class ExplorationSettings
{
    // Slots in each ten given to a random post from below FromRank. 0 stops the ranker learning about posts it
    // wouldn't pick, so its training data narrows over time.
    public int Slots { get; set; } = 1;

    public int FromRank { get; set; } = 30;
}

[JsonConverter(typeof(JsonStringEnumConverter<BoostMatch>))]
public enum BoostMatch
{
    [JsonStringEnumMemberName("tag")]
    Tag,
    [JsonStringEnumMemberName("author")]
    Author,
    [JsonStringEnumMemberName("hasMedia")]
    HasMedia,
    [JsonStringEnumMemberName("following")]
    Following,
    [JsonStringEnumMemberName("notFollowing")]
    NotFollowing,
}

// Multiplies the score of posts that match: 1.5 lifts them, 0.5 lowers them, 0 sinks them to the bottom
public sealed class BoostRule
{
    public BoostMatch Match { get; set; }

    // The tag (without #) for Tag, the username (without @) for Author
    public string? Value { get; set; }

    public double Multiplier { get; set; } = 1.5;
}

public sealed class FilterSettings
{
    // Posts with any of these tags are left out
    public List<string> ExcludeTags { get; set; } = [];

    // When not empty, only posts with at least one of these tags are shown
    public List<string> OnlyTags { get; set; } = [];

    public bool MediaOnly { get; set; }

    public int MinLikes { get; set; }
}

public sealed record ProfileError(string Field, string Message, int? Position = null);
