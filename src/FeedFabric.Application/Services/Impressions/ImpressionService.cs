using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Impressions;

public class ImpressionService(AppDbContext db, IConnectionMultiplexer redis, FeedLog feedLog, TimeProvider timeProvider) : IImpressionService
{
    public static readonly TimeSpan SeenWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinuteLifetime = TimeSpan.FromDays(2);
    private const int MaxPerRequest = 100;

    // One view longer than this is someone who left the tab open, not reading
    public static readonly TimeSpan MaxDwell = TimeSpan.FromSeconds(60);

    public async Task<int?> RecordAsync(string externalUserId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }

        var ids = postIds.Distinct().Take(MaxPerRequest).ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var database = redis.GetDatabase();
        // First round trip: which of these hasn't this viewer seen lately (SET NX with an expiry)
        var marks = ids.Select(id => database.StringSetAsync(ImpressionKeys.Seen(viewerId.Value, id), 1, SeenWindow, When.NotExists)).ToList();
        var results = await Task.WhenAll(marks);
        await feedLog.MarkAsync(viewerId.Value, ids, FeedSignal.Seen, cancellationToken);
        var fresh = ids.Where((_, i) => results[i]).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        // Second: count them
        var minute = ImpressionKeys.Minute(timeProvider.GetUtcNow().UtcDateTime);
        var batch = database.CreateBatch();
        var writes = new List<Task>(fresh.Count * 2 + 2);
        foreach (var id in fresh)
        {
            writes.Add(batch.SortedSetIncrementAsync(ImpressionKeys.PostTotals, id.ToString(), 1));
            writes.Add(batch.HyperLogLogAddAsync(ImpressionKeys.Viewers(id), viewerId.Value.ToString()));
        }
        writes.Add(batch.StringIncrementAsync(minute, fresh.Count));
        writes.Add(batch.KeyExpireAsync(minute, MinuteLifetime));
        batch.Execute();
        await Task.WhenAll(writes);
        return fresh.Count;
    }

    public async Task<IReadOnlyList<(Guid PostId, long Impressions)>> TopPostsAsync(int count)
    {
        var top = await redis.GetDatabase().SortedSetRangeByRankWithScoresAsync(ImpressionKeys.PostTotals, 0, count - 1, Order.Descending);
        return top.Select(e => (Guid.Parse(e.Element.ToString()), (long)e.Score)).ToList();
    }

    public async Task<bool?> RecordOpenAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }

        var database = redis.GetDatabase();
        await feedLog.MarkAsync(viewerId.Value, postId, FeedSignal.Opened, cancellationToken);
        if (!await database.StringSetAsync(ImpressionKeys.Opened(viewerId.Value, postId), 1, SeenWindow, When.NotExists))
        {
            return false;
        }

        var minute = ImpressionKeys.OpensMinute(timeProvider.GetUtcNow().UtcDateTime);
        var batch = database.CreateBatch();
        var writes = new List<Task>
        {
            batch.SortedSetIncrementAsync(ImpressionKeys.OpenTotals, postId.ToString(), 1),
            batch.StringIncrementAsync(minute),
            batch.KeyExpireAsync(minute, MinuteLifetime),
        };
        batch.Execute();
        await Task.WhenAll(writes);
        return true;
    }

    public async Task<int?> RecordDwellAsync(string externalUserId, IReadOnlyCollection<(Guid PostId, int Ms)> views, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }

        var timed = views.Where(v => v.Ms > 0).Take(MaxPerRequest).Select(v => (v.PostId, Ms: Math.Min(v.Ms, (int)MaxDwell.TotalMilliseconds))).ToList();
        if (timed.Count == 0)
        {
            return 0;
        }
        await feedLog.AddDwellAsync(viewerId.Value, timed, cancellationToken);

        var minute = ImpressionKeys.DwellMinute(timeProvider.GetUtcNow().UtcDateTime);
        var batch = redis.GetDatabase().CreateBatch();
        var writes = new List<Task>(timed.Count * 6 + 1);
        foreach (var (postId, ms) in timed)
        {
            var bucket = DwellBuckets.For(ms);
            foreach (var key in new[] { ImpressionKeys.PostDwell(postId), minute })
            {
                writes.Add(batch.HashIncrementAsync(key, "ms", ms));
                writes.Add(batch.HashIncrementAsync(key, "n", 1));
                writes.Add(batch.HashIncrementAsync(key, bucket, 1));
            }
        }
        writes.Add(batch.KeyExpireAsync(minute, MinuteLifetime));
        batch.Execute();
        await Task.WhenAll(writes);
        return timed.Count;
    }

    public async Task<IReadOnlyDictionary<Guid, PostSignals>> SignalsAsync(IReadOnlyCollection<Guid> postIds)
    {
        var database = redis.GetDatabase();
        var ids = postIds.Distinct().ToList();
        var totals = ids.Select(id => database.SortedSetScoreAsync(ImpressionKeys.PostTotals, id.ToString())).ToList();
        var viewers = ids.Select(id => database.HyperLogLogLengthAsync(ImpressionKeys.Viewers(id))).ToList();
        var opens = ids.Select(id => database.SortedSetScoreAsync(ImpressionKeys.OpenTotals, id.ToString())).ToList();
        var dwell = ids.Select(id => database.HashGetAsync(ImpressionKeys.PostDwell(id), ["n", "ms"])).ToList();
        await Task.WhenAll(totals.Cast<Task>().Concat(viewers).Concat(opens).Concat(dwell));
        return ids.Select((id, i) => (id, new PostSignals(
                (long)(totals[i].Result ?? 0), viewers[i].Result, (long)(opens[i].Result ?? 0),
                Long(dwell[i].Result[0]), Long(dwell[i].Result[1]))))
            .ToDictionary(x => x.id, x => x.Item2);
    }

    public async Task<IReadOnlyList<MinuteSignals>> PerMinuteAsync(int minutes)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var range = Enumerable.Range(0, minutes).Select(i => current.AddMinutes(i - minutes + 1)).ToList();
        var database = redis.GetDatabase();
        var impressions = database.StringGetAsync(range.Select(m => (RedisKey)ImpressionKeys.Minute(m)).ToArray());
        var opens = database.StringGetAsync(range.Select(m => (RedisKey)ImpressionKeys.OpensMinute(m)).ToArray());
        var fields = new RedisValue[] { "n", "ms" }.Concat(DwellBuckets.All.Select(b => (RedisValue)b.Field)).ToArray();
        var dwell = range.Select(m => database.HashGetAsync(ImpressionKeys.DwellMinute(m), fields)).ToList();
        await Task.WhenAll(dwell.Cast<Task>().Append(impressions).Append(opens));

        return range.Select((m, i) =>
        {
            var d = dwell[i].Result;
            var buckets = DwellBuckets.All.Select((b, j) => (b.Field, Count: Long(d[j + 2]))).ToDictionary(x => x.Field, x => x.Count);
            return new MinuteSignals(m, Long(impressions.Result[i]), Long(opens.Result[i]), Long(d[0]), Long(d[1]), buckets);
        }).ToList();
    }

    private static long Long(RedisValue value) => value.HasValue ? (long)value : 0;
}
