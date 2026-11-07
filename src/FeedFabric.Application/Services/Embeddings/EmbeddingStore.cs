using StackExchange.Redis;

namespace FeedFabric.Application.Services.Embeddings;

// Post embeddings in Redis: emb:{postId} (384 float32s) for a few days, and emb:recent (post ID by creation time)
// so readers can pick up what's new. The worker writes them as posts are created.
public class EmbeddingStore(IConnectionMultiplexer redis)
{
    public const string RecentKey = "emb:recent";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(3);

    private static string Key(Guid postId) => $"emb:{postId}";

    public async Task PutAsync(Guid postId, DateTime createdAt, float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        var database = redis.GetDatabase();
        await database.StringSetAsync(Key(postId), bytes, Lifetime);
        await database.SortedSetAddAsync(RecentKey, postId.ToString(), new DateTimeOffset(createdAt, TimeSpan.Zero).ToUnixTimeMilliseconds());
    }

    public async Task<Dictionary<Guid, float[]>> GetAsync(IReadOnlyCollection<Guid> postIds)
    {
        if (postIds.Count == 0)
        {
            return [];
        }
        var ids = postIds.Distinct().ToList();
        var values = await redis.GetDatabase().StringGetAsync(ids.Select(id => (RedisKey)Key(id)).ToArray());
        var result = new Dictionary<Guid, float[]>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (values[i].HasValue)
            {
                var bytes = (byte[])values[i]!;
                var vector = new float[bytes.Length / sizeof(float)];
                Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
                result[ids[i]] = vector;
            }
        }
        return result;
    }

    // Posts with embeddings created after the given time, oldest first
    public async Task<List<(Guid PostId, long CreatedMs)>> RecentAsync(long afterMs, int take)
    {
        var entries = await redis.GetDatabase().SortedSetRangeByScoreWithScoresAsync(RecentKey, afterMs, double.PositiveInfinity,
            Exclude.Start, Order.Ascending, 0, take);
        return entries.Select(e => (Guid.Parse(e.Element.ToString()), (long)e.Score)).ToList();
    }

    // A deleted account's posts
    public async Task RemoveAsync(IReadOnlyCollection<Guid> postIds)
    {
        var database = redis.GetDatabase();
        foreach (var chunk in postIds.Chunk(500))
        {
            await database.KeyDeleteAsync(chunk.Select(id => (RedisKey)Key(id)).ToArray());
            await database.SortedSetRemoveAsync(RecentKey, chunk.Select(id => (RedisValue)id.ToString()).ToArray());
        }
    }

    public async Task TrimAsync(DateTime olderThan) =>
        await redis.GetDatabase().SortedSetRemoveRangeByScoreAsync(RecentKey, double.NegativeInfinity,
            new DateTimeOffset(olderThan, TimeSpan.Zero).ToUnixTimeMilliseconds());
}
