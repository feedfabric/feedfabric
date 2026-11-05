using Microsoft.Extensions.DependencyInjection;

namespace FeedFabric.Application.Services.Embeddings;

// The last day's post embeddings in memory, for "posts that mean something close to what you like": a brute-force
// nearest-neighbour search, a few milliseconds over tens of thousands of posts. Picks up new posts from Redis at most
// every 30 seconds. At millions of posts this becomes a vector index (pgvector, Redis search, Qdrant…).
public sealed class EmbeddingIndex(IServiceScopeFactory scopes, TimeProvider timeProvider)
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, (float[] Vector, long CreatedMs)> posts = [];
    private long latestMs;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<(Guid PostId, float Similarity)>> NearestAsync(float[] query, int take, ISet<Guid> exclude, CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        lock (posts)
        {
            return posts.Where(p => !exclude.Contains(p.Key))
                .Select(p => (p.Key, TextEmbedder.Cosine(query, p.Value.Vector)))
                .OrderByDescending(p => p.Item2)
                .Take(take)
                .ToList();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() < nextRefresh)
        {
            return;
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (timeProvider.GetUtcNow() < nextRefresh)
            {
                return;
            }
            nextRefresh = timeProvider.GetUtcNow().Add(RefreshEvery);

            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<EmbeddingStore>();
            var cutoff = timeProvider.GetUtcNow().Add(-Window).ToUnixTimeMilliseconds();
            var from = Math.Max(latestMs, cutoff);
            while (true)
            {
                var fresh = await store.RecentAsync(from, 2000);
                if (fresh.Count == 0)
                {
                    break;
                }
                var vectors = await store.GetAsync(fresh.Select(f => f.PostId).ToList());
                lock (posts)
                {
                    foreach (var (postId, createdMs) in fresh)
                    {
                        if (vectors.TryGetValue(postId, out var vector))
                        {
                            posts[postId] = (vector, createdMs);
                        }
                    }
                }
                from = fresh[^1].CreatedMs;
                latestMs = from;
            }

            lock (posts)
            {
                foreach (var old in posts.Where(p => p.Value.CreatedMs < cutoff).Select(p => p.Key).ToList())
                {
                    posts.Remove(old);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
