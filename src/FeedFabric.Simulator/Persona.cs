namespace FeedFabric.Simulator;

// A simulated user's voice. Key matches the first column of Data/posts.tsv; Handle prefixes their username (runs_1, walks_2, ...)
public record Persona(string Key, string Handle)
{
    // sim-1 to sim-12 each get a different persona, then the cycle repeats
    public static readonly Persona[] All =
    [
        new("runner", "runs"),
        new("walker", "walks"),
        new("globetrotter", "travels"),
        new("yogi", "flows"),
        new("lifter", "lifts"),
        new("water_wheels", "rides"),
        new("habit_builder", "habits"),
        new("recovering", "recovers"),
        new("new_parent", "parenting"),
        new("desk_worker", "desk"),
        new("silver_stride", "stride"),
        new("beginner", "beginner"),
    ];

    public static Persona ForUser(int number) => All[(number - 1) % All.Length];

    public string Username(int number) => $"{Handle}_{number}";
}
