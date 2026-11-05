namespace FeedFabric.Application.Services.Ranking.Profiles;

// Ready-made profiles: read-only, always available, and the starting point for your own ("Save as")
public static class RankingPresets
{
    public const string Balanced = "balanced";

    public static readonly IReadOnlyDictionary<string, Func<RankingProfile>> All = new Dictionary<string, Func<RankingProfile>>
    {
        [Balanced] = () => new RankingProfile
        {
            Name = "Balanced",
            Description = "The default: people you follow plus the best of everything else, ranked by the trained model.",
        },

        ["community"] = () => new RankingProfile
        {
            Name = "Community",
            Description = "Conversation first: posts people reply to, from people you know and people like you.",
            Scoring = new ScoringSettings { Goals = new Goals { Open = 0.3, Like = 0.8, Comment = 5, LongRead = 2, Skip = -1.5 } },
            Sources = new SourceSettings { SimilarMeaning = new SourceSetting { Limit = 50 } },
            Boosts = [new BoostRule { Match = BoostMatch.Following, Multiplier = 1.3 }],
        },

        ["discovery"] = () => new RankingProfile
        {
            Name = "Discovery",
            Description = "New people and ideas: more from outside your follows, more exploring, less of the same author.",
            Sources = new SourceSettings
            {
                Following = new SourceSetting { Limit = 100 },
                Topics = new SourceSetting { Limit = 200 },
                SimilarReaders = new SourceSetting { Limit = 200 },
                SimilarMeaning = new SourceSetting { Limit = 200 },
                TopTopics = 5,
                LookbackHours = 24,
            },
            Variety = new VarietySettings { AuthorCap = 1, OutOfNetwork = 5 },
            Exploration = new ExplorationSettings { Slots = 2, FromRank = 20 },
        },

        ["following-first"] = () => new RankingProfile
        {
            Name = "Following first",
            Description = "Mostly people you follow, ranked; a little from elsewhere to keep it fresh.",
            Sources = new SourceSettings
            {
                Topics = new SourceSetting { Limit = 30 },
                SimilarReaders = new SourceSetting { Enabled = false },
                SimilarMeaning = new SourceSetting { Limit = 30 },
            },
            Variety = new VarietySettings { OutOfNetwork = 1 },
            Boosts = [new BoostRule { Match = BoostMatch.Following, Multiplier = 2 }],
        },

        ["fresh"] = () => new RankingProfile
        {
            Name = "Fresh",
            Description = "What's happening now: strongly favours the last few hours and posts picking up speed.",
            Scoring = new ScoringSettings
            {
                Mode = ScoringMode.Expression,
                Expression = "formula * decay_exp(age_hours, 3) * (1 + 0.3 * log1p(velocity_30m))",
            },
            Freshness = new FreshnessSettings { MaxAgeHours = 48 },
            Sources = new SourceSettings { LookbackHours = 6 },
        },

        ["newest"] = () => new RankingProfile
        {
            Name = "Newest first",
            Description = "No ranking: everything from your sources, newest at the top.",
            Scoring = new ScoringSettings { Mode = ScoringMode.Newest },
            Variety = new VarietySettings { AuthorCap = DiversityRules.Window, TopicRun = DiversityRules.Window, OutOfNetwork = 0, SeenPenalty = 1 },
            Exploration = new ExplorationSettings { Slots = 0 },
        },
    };

    public static bool IsPreset(string key) => All.ContainsKey(key);
}
