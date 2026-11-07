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
        if (take <= 0)
        {
            return [];
        }

        // Keeps only the best `take` so far in a min-heap, rather than scoring and sorting every post: the work and
        // memory stay at the size of the answer, not of the index
        var best = new PriorityQueue<Guid, float>(take + 1);
        lock (posts)
        {
            foreach (var (postId, entry) in posts)
            {
                if (exclude.Contains(postId))
                {
                    continue;
                }
                var similarity = TextEmbedder.Cosine(query, entry.Vector);
                if (best.Count < take)
                {
                    best.Enqueue(postId, similarity);
                }
                else if (best.TryPeek(out _, out var worst) && similarity > worst)
                {
                    best.DequeueEnqueue(postId, similarity);
                }
            }
        }

        var nearest = new (Guid PostId, float Similarity)[best.Count];
        for (var i = nearest.Length - 1; best.TryDequeue(out var postId, out var similarity); i--)
        {
            nearest[i] = (postId, similarity);
        }
        return nearest;
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
