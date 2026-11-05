namespace FeedFabric.Application.Services.Impressions;

public interface IImpressionService
{
    // Counts each post as seen by the viewer, at most once per viewer per post every SeenWindow.
    // Returns how many were new; false if the user hasn't been synced.
    Task<int?> RecordAsync(string externalUserId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<(Guid PostId, long Impressions)>> TopPostsAsync(int count);

    // The viewer opened the post (its page): at most once per viewer per post every SeenWindow.
    // True if it counted; null if the user hasn't been synced.
    Task<bool?> RecordOpenAsync(string externalUserId, Guid postId, CancellationToken cancellationToken);

    // How long each post was on screen for one view, in ms; each view is capped at MaxDwell. Null if the user hasn't been synced.
    Task<int?> RecordDwellAsync(string externalUserId, IReadOnlyCollection<(Guid PostId, int Ms)> views, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, PostSignals>> SignalsAsync(IReadOnlyCollection<Guid> postIds);

    // One row per minute, oldest first, ending with the current minute
    Task<IReadOnlyList<MinuteSignals>> PerMinuteAsync(int minutes);
}

public sealed record PostSignals(long Impressions, long Viewers, long Opens, long DwellViews, long DwellMs)
{
    public double? AverageDwellSeconds => DwellViews == 0 ? null : Math.Round(DwellMs / 1000.0 / DwellViews, 1);
}

public sealed record MinuteSignals(DateTime Minute, long Impressions, long Opens, long DwellViews, long DwellMs, IReadOnlyDictionary<string, long> DwellBuckets);
