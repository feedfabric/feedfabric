namespace FeedFabric.Application.Services.Impressions;

// Impressions, opens and dwell live in Redis only: they're high volume, and losing a few to a restart doesn't matter
public static class ImpressionKeys
{
    // Set for a while after a viewer sees a post, so scrolling past it again isn't counted again
    public static string Seen(Guid viewerId, Guid postId) => $"imp:seen:{viewerId}:{postId}";

    // A post's own signal keys (Viewers, PostDwell) expire this long after it was last seen; the retention job also
    // drops old posts from the totals below
    public static readonly TimeSpan PostSignalLifetime = TimeSpan.FromDays(30);

    // Sorted set of every post's total impressions
    public const string PostTotals = "impressions:posts";

    // HyperLogLog of a post's distinct viewers: approximate, a few hundred bytes however many people see it
    public static string Viewers(Guid postId) => $"post:{postId}:viewers";

    // Impressions counted in one minute (UTC), for charts
    public static string Minute(DateTime utc) => $"impressions:minute:{utc:yyyyMMddHHmm}";

    // Set for a while after a viewer opens a post, so coming back to it isn't another open
    public static string Opened(Guid viewerId, Guid postId) => $"open:seen:{viewerId}:{postId}";

    // Sorted set of every post's total opens
    public const string OpenTotals = "opens:posts";

    public static string OpensMinute(DateTime utc) => $"opens:minute:{utc:yyyyMMddHHmm}";

    // Hash per post: "ms" (total time on screen), "n" (views timed) and one count per DwellBuckets bucket
    public static string PostDwell(Guid postId) => $"post:{postId}:dwell";

    // The same hash for everything timed in one minute, for the platform's average and distribution
    public static string DwellMinute(DateTime utc) => $"dwell:minute:{utc:yyyyMMddHHmm}";
}

// How long a post stayed on screen, in the buckets the dashboard shows
public static class DwellBuckets
{
    public static readonly (string Field, string Label, int UpToMs)[] All =
    [
        ("lt1s", "Under 1s", 1000),
        ("1to3s", "1–3s", 3000),
        ("3to10s", "3–10s", 10000),
        ("10s", "10s or more", int.MaxValue),
    ];

    public static string For(int ms) => All.First(b => ms < b.UpToMs).Field;
}
