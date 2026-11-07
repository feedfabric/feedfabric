using System.Diagnostics.Metrics;

namespace FeedFabric.Application.Services;

// Metrics the domain code records, exported by OpenTelemetry in whichever process runs it (meter "FeedFabric")
public static class AppMetrics
{
    public const string MeterName = "FeedFabric";

    private static readonly Meter Meter = new(MeterName);

    // Every post and comment checked, by outcome ("allowed", "block", "hold", "flag"); held and blocked rates are
    // those outcomes over the total
    public static readonly Counter<long> ModerationChecks = Meter.CreateCounter<long>(
        "feedfabric.moderation.checks", "{check}", "Posts and comments checked, by outcome and category");

    // For You rankings, by what scored them; fallback=true when the profile wanted the model and it wasn't loaded
    public static readonly Counter<long> Rankings = Meter.CreateCounter<long>(
        "feedfabric.ranking.rankings", "{ranking}", "For You rankings, by ranker and whether the model was missing");
}
