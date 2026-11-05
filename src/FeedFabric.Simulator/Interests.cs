namespace FeedFabric.Simulator;

// What a simulated user is into, so they don't behave like a persona robot: their persona most (1), one or two other
// personas' topics somewhat (SecondaryInterest), everything else a little (BaseInterest), plus their own appetite
// for something new. Worked out from the user number with a fixed seed, so a user is the same on every run.
public sealed class Interests
{
    private readonly Dictionary<string, double> byPersona = [];
    private readonly double baseInterest;

    public double Novelty { get; }

    private Interests(int number, SimulatorOptions settings)
    {
        var random = new Random(number * 7919 + settings.Seed);
        baseInterest = settings.BaseInterest;
        byPersona[Persona.ForUser(number).Key] = 1;
        var others = Persona.All.Where(p => p != Persona.ForUser(number)).OrderBy(_ => random.Next()).Take(1 + random.Next(2));
        foreach (var other in others)
        {
            byPersona[other.Key] = settings.SecondaryInterest * (0.7 + 0.6 * random.NextDouble());
        }
        Novelty = settings.MaxNovelty * random.NextDouble();
    }

    public static Interests For(int number, SimulatorOptions settings) => new(number, settings);

    // How interested they are in a post by this author (a simulated user; others count as base interest)
    public double In(string authorExternalUserId) =>
        authorExternalUserId.StartsWith(Simulation.UserPrefix) && int.TryParse(authorExternalUserId[Simulation.UserPrefix.Length..], out var n)
            ? byPersona.GetValueOrDefault(Persona.ForUser(n).Key, baseInterest)
            : baseInterest;

    public bool IsOffTopic(string authorExternalUserId) => In(authorExternalUserId) <= baseInterest;
}
