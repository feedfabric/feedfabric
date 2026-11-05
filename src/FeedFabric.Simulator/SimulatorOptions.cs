using FeedFabric.Application.Services.Ranking.Profiles;

namespace FeedFabric.Simulator;

public class SimulatorOptions
{
    public const string Section = "Simulator";

    // Simulated users are sim-1 to sim-{Users}; sim-1 is the most followed
    public int Users { get; set; } = 1000;

    // How many accounts each user follows, picked uniformly in this range
    public int MinFollowing { get; set; } = 5;

    public int MaxFollowing { get; set; } = 150;

    // Zipf exponent for who gets followed: higher means a few accounts get most of the followers
    public double PopularityExponent { get; set; } = 1.0;

    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int PostsPerTick { get; set; } = 5;

    // Each tick this many simulated users read their home feed and may like what they see
    public int ReadersPerTick { get; set; } = 10;

    // How far down their home feed each reader scrolls
    public int PostsReadPerVisit { get; set; } = 20;

    // The ranking profiles For You readers are split between, so the dashboard can compare them on the same
    // population: reader 2 gets the first, reader 4 the second, and so on round the list
    public List<string> ForYouProfiles { get; set; } = [RankingPresets.Balanced];

    // How interested a reader is in a post (see Interests): 1 for their persona, about this for one or two other
    // personas they also like, BaseInterest for everything else, plus up to MaxNovelty for whatever's new
    public double SecondaryInterest { get; set; } = 0.45;

    public double BaseInterest { get; set; } = 0.08;

    public double MaxNovelty { get; set; } = 0.15;

    // Chances of opening and liking a post they're fully interested in at the top of the feed; lower down and less
    // interested, proportionally less
    public double OpenChance { get; set; } = 0.25;

    public double LikeChance { get; set; } = 0.35;

    // How quickly attention falls with position: interest × 1 / (1 + PositionDecay × position)
    public double PositionDecay { get; set; } = 0.06;

    // Chance of pressing "Not interested" on an off-topic post
    public double NotInterestedChance { get; set; } = 0.015;

    // Chance of pressing "Show more" on a post that fully interests them at the top of the feed; less further down
    public double ShowMoreChance { get; set; } = 0.01;

    // Chance of commenting on a post the reader just liked, and of replying to a comment already there instead
    public double CommentChance { get; set; } = 0.15;

    public double ReplyChance { get; set; } = 0.4;

    // Chance a comment @mentions the person it answers, and a post @mentions someone with the same persona
    public double CommentMentionChance { get; set; } = 0.3;

    public double PostMentionChance { get; set; } = 0.05;

    // On an empty platform, days of history to create first from the conversation library, and how much per day
    public int BackfillDays { get; set; } = 14;

    public int BackfillThreadsPerDay { get; set; } = 140;

    public int BackfillMinLikes { get; set; } = 2;

    public int BackfillMaxLikes { get; set; } = 18;

    // Share of live posts that start a conversation from the library (a question, a milestone, a discussion);
    // the rest are everyday updates from Data/posts.tsv
    public double ConversationShare { get; set; } = 0.35;

    // Same seed, same follow graph
    public int Seed { get; set; } = 42;
}
