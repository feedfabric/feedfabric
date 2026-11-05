using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Posts;

namespace FeedFabric.Worker.Events.Embeddings;

// Embeds a post's text as it's created or edited, for "posts that mean something close to what you like".
// Embeddings are a nice-to-have: without the model (make models) posts are skipped, not held up.
public class PostEmbeddingHandler(AppDbContext db, TextEmbedder embedder, EmbeddingStore store, ILogger<PostEmbeddingHandler> logger)
{
    private static bool warned;

    public async Task HandleAsync(EventEnvelope<PostCreatedEvent> envelope, CancellationToken cancellationToken)
    {
        if (!await embedder.IsAvailableAsync(cancellationToken))
        {
            if (!warned)
            {
                logger.LogWarning("No embedding model (run make models); posts aren't being embedded");
                warned = true;
            }
            return;
        }

        var post = await db.Posts.AsNoTracking()
            .Where(p => p.Id == envelope.Data.PostId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.Content, p.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (post is null)
        {
            return;
        }

        var vector = (await embedder.EmbedAsync([post.Content], cancellationToken))[0];
        await store.PutAsync(post.Id, post.CreatedAt, vector);
    }

    // On startup: the last day's posts that have no embedding yet (posted while the worker or model was missing)
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        if (!await embedder.IsAvailableAsync(cancellationToken))
        {
            return;
        }

        var since = DateTime.UtcNow.AddHours(-24);
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.CreatedAt >= since && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Content, p.CreatedAt })
            .ToListAsync(cancellationToken);
        var have = await store.GetAsync(posts.Select(p => p.Id).ToList());
        var missing = posts.Where(p => !have.ContainsKey(p.Id)).ToList();
        foreach (var batch in missing.Chunk(64))
        {
            var vectors = await embedder.EmbedAsync(batch.Select(p => p.Content).ToList(), cancellationToken);
            for (var i = 0; i < batch.Length; i++)
            {
                await store.PutAsync(batch[i].Id, batch[i].CreatedAt, vectors[i]);
            }
        }
        await store.TrimAsync(DateTime.UtcNow - EmbeddingStore.Lifetime);
        if (missing.Count > 0)
        {
            logger.LogInformation("Embedded {Count} posts from the last day", missing.Count);
        }
    }
}
