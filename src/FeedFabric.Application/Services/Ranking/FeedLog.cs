using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Ranking;

public enum FeedSignal
{
    Seen,
    Opened,
    Liked,
    Commented,
    NotInterested,
    ShowMore,
}

// Writes FeedLogEntity rows as feeds are served, and fills in their labels as viewers act on the posts
public class FeedLog(AppDbContext db, IConnectionMultiplexer redis, TimeProvider timeProvider)
{
    // A post served again within this window isn't logged again, so feeds that refresh don't flood the log
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(30);

    // An action counts towards a serve made at most this long before it
    public static readonly TimeSpan AttributionWindow = TimeSpan.FromHours(1);

    public async Task LogServedAsync(Guid viewerId, string feed, string ranker,
        IReadOnlyList<ServedPost> served, CancellationToken cancellationToken, string? profile = null)
    {
        if (served.Count == 0)
        {
            return;
        }

        var database = redis.GetDatabase();
        var fresh = await Task.WhenAll(served.Select(s =>
            database.StringSetAsync($"served:{feed}:{viewerId}:{s.PostId}", 1, RepeatWindow, When.NotExists)));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var position = RankingFeatures.Index("position");
        var rows = served.Where((_, i) => fresh[i]).Select(s =>
        {
            // The position it was actually shown at, not the 0 it was scored with
            var features = (double[])s.Features.Clone();
            features[position] = s.Position;
            return new FeedLogEntity
            {
                ViewerId = viewerId,
                PostId = s.PostId,
                Feed = feed,
                Ranker = ranker,
                Profile = profile,
                Position = s.Position,
                Score = s.Score,
                Features = JsonSerializer.Serialize(RankingFeatures.ToDictionary(features)),
                ServedAt = now,
                Explored = s.Explored,
                Propensity = s.Propensity,
            };
        }).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        db.FeedLog.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    public Task MarkAsync(Guid viewerId, Guid postId, FeedSignal signal, CancellationToken cancellationToken) =>
        MarkAsync(viewerId, [postId], signal, cancellationToken);

    public async Task MarkAsync(Guid viewerId, IReadOnlyCollection<Guid> postIds, FeedSignal signal, CancellationToken cancellationToken)
    {
        var rows = Recent(viewerId, postIds);
        _ = signal switch
        {
            FeedSignal.Seen => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.Seen, true), cancellationToken),
            // Opening or engaging with a post means it was seen, whatever the impression tracking caught
            FeedSignal.Opened => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.Opened, true).SetProperty(f => f.Seen, true), cancellationToken),
            FeedSignal.Liked => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.Liked, true).SetProperty(f => f.Seen, true), cancellationToken),
            FeedSignal.Commented => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.Commented, true).SetProperty(f => f.Seen, true), cancellationToken),
            // The two cancel each other out: the latest press is what the viewer meant
            FeedSignal.NotInterested => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.NotInterested, true).SetProperty(f => f.ShowMore, false).SetProperty(f => f.Seen, true), cancellationToken),
            FeedSignal.ShowMore => await rows.ExecuteUpdateAsync(s => s.SetProperty(f => f.ShowMore, true).SetProperty(f => f.NotInterested, false).SetProperty(f => f.Seen, true), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(signal)),
        };
    }

    // One UPDATE for the whole beacon rather than one per post: each post's total is joined in from a pair of arrays
    public async Task AddDwellAsync(Guid viewerId, IReadOnlyCollection<(Guid PostId, int Ms)> views, CancellationToken cancellationToken)
    {
        var totals = views.GroupBy(v => v.PostId).Select(g => (PostId: g.Key, Ms: g.Sum(v => v.Ms))).ToList();
        if (totals.Count == 0)
        {
            return;
        }
        var postIds = totals.Select(t => t.PostId).ToArray();
        var ms = totals.Select(t => t.Ms).ToArray();
        var since = timeProvider.GetUtcNow().UtcDateTime - AttributionWindow;
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "FeedLog" f SET "DwellMs" = f."DwellMs" + v.ms
            FROM unnest({postIds}, {ms}) AS v(post_id, ms)
            WHERE f."ViewerId" = {viewerId} AND f."PostId" = v.post_id AND f."ServedAt" >= {since}
            """, cancellationToken);
    }

    private IQueryable<FeedLogEntity> Recent(Guid viewerId, IReadOnlyCollection<Guid> postIds)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime - AttributionWindow;
        return db.FeedLog.Where(f => f.ViewerId == viewerId && postIds.Contains(f.PostId) && f.ServedAt >= since);
    }
}

public sealed record ServedPost(Guid PostId, int Position, double Score, double[] Features, bool Explored = false, double Propensity = 1);
