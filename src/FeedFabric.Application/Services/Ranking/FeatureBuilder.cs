using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Likes;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Ranking;

// Works out RankingFeatures for one viewer and a batch of posts, in a handful of batched queries
public class FeatureBuilder(AppDbContext db, IConnectionMultiplexer redis, IImpressionService impressions, EmbeddingStore embeddings, TimeProvider timeProvider)
{
    // Smoothing for rates: a post with no impressions yet is assumed to do about as well as an average one
    private const double PriorImpressions = 20;
    private const double PriorOpens = 1;
    private const double PriorEngagements = 1;
    private const double PriorAuthorSeen = 10;
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SessionWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AuthorHistory = TimeSpan.FromDays(7);
    private const int ProfileLikes = 30;

    private float[]? viewerVector;
    private Guid? viewerVectorFor;

    // What the viewer's recent likes and comments are about: the average of those posts' embeddings, or null
    // without any (or without the embedding model)
    public async Task<float[]?> ViewerVectorAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        if (viewerVectorFor == viewerId)
        {
            return viewerVector;
        }
        var liked = await db.Likes.Where(l => l.UserId == viewerId).OrderByDescending(l => l.CreatedAt).Take(ProfileLikes)
            .Select(l => l.PostId).ToListAsync(cancellationToken);
        var commented = await db.Comments.Where(c => c.AuthorId == viewerId).OrderByDescending(c => c.CreatedAt).Take(ProfileLikes / 2)
            .Select(c => c.PostId).ToListAsync(cancellationToken);
        var vectors = await embeddings.GetAsync(liked.Concat(commented).Distinct().ToList());
        viewerVector = TextEmbedder.Mean(vectors.Values);
        viewerVectorFor = viewerId;
        return viewerVector;
    }

    public async Task<Dictionary<Guid, double[]>> BuildAsync(Guid viewerId, IReadOnlyList<HomeFeedPostResponse> posts, CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts.Select(p => p.Id).Distinct().ToList();
        var authorIds = posts.Select(p => p.AuthorId).Distinct().ToList();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var follows = (await db.Follows.Where(f => f.FollowerId == viewerId && authorIds.Contains(f.FolloweeId))
            .Select(f => f.FolloweeId).ToListAsync(cancellationToken)).ToHashSet();
        var followers = await db.Follows.Where(f => authorIds.Contains(f.FolloweeId))
            .GroupBy(f => f.FolloweeId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var likes = await db.Likes.Where(l => postIds.Contains(l.PostId))
            .GroupBy(l => l.PostId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var comments = await db.Comments.Where(c => postIds.Contains(c.PostId) && c.DeletedAt == null)
            .GroupBy(c => c.PostId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var withMedia = (await db.PostMedia.Where(m => postIds.Contains(m.PostId)).Select(m => m.PostId).Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var tags = (await db.PostTags.Where(t => postIds.Contains(t.PostId)).Select(t => new { t.PostId, t.Tag, t.Confidence })
            .ToListAsync(cancellationToken)).ToLookup(t => t.PostId);

        // How fast each post is picking up likes and comments right now
        var recentSince = now - VelocityWindow;
        var velocity = await db.Likes.Where(l => postIds.Contains(l.PostId) && l.CreatedAt >= recentSince).Select(l => l.PostId)
            .Concat(db.Comments.Where(c => postIds.Contains(c.PostId) && c.CreatedAt >= recentSince).Select(c => c.PostId))
            .GroupBy(id => id).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        // The viewer's own history with each author: how often they opened that author's posts after seeing them
        var historySince = now - AuthorHistory;
        var withAuthor = await db.FeedLog.Where(f => f.ViewerId == viewerId && f.Seen && f.ServedAt >= historySince)
            .Join(db.Posts.Where(p => authorIds.Contains(p.AuthorId)), f => f.PostId, p => p.Id, (f, p) => new { p.AuthorId, f.Opened })
            .GroupBy(x => x.AuthorId).Select(g => new { g.Key, Seen = g.Count(), Opened = g.Count(x => x.Opened) })
            .ToDictionaryAsync(x => x.Key, cancellationToken);

        // What the viewer has engaged with in the last 15 minutes: the mood of this session
        var sessionSince = now - SessionWindow;
        var sessionPosts = await db.FeedLog.Where(f => f.ViewerId == viewerId && f.ServedAt >= sessionSince && (f.Opened || f.Liked || f.Commented))
            .Select(f => f.PostId).Distinct().ToListAsync(cancellationToken);
        var sessionTopics = (await db.PostTags.Where(t => sessionPosts.Contains(t.PostId)).Select(t => new { t.Tag, t.Confidence }).ToListAsync(cancellationToken))
            .GroupBy(t => t.Tag).ToDictionary(g => g.Key, g => g.Sum(t => t.Confidence));
        var sessionTotal = sessionTopics.Values.Sum();

        var viewer = await ViewerVectorAsync(viewerId, cancellationToken);
        var postVectors = viewer is null ? [] : await embeddings.GetAsync(postIds);

        var database = redis.GetDatabase();
        // Every author's affinity in one ZMSCORE rather than a command each
        var affinityTask = database.SortedSetScoresAsync(EngagementKeys.Affinity(viewerId), authorIds.Select(a => (RedisValue)a.ToString()).ToArray());
        var topicsTask = database.SortedSetRangeByRankWithScoresAsync(EngagementKeys.Topics(viewerId), 0, -1, Order.Descending);
        var signalsTask = impressions.SignalsAsync(postIds);
        await Task.WhenAll(affinityTask, topicsTask, signalsTask);

        var affinity = authorIds.Select((a, i) => (a, Score: affinityTask.Result[i] ?? 0)).ToDictionary(x => x.a, x => x.Score);
        var topics = topicsTask.Result.Where(t => t.Score > 0).ToDictionary(t => t.Element.ToString(), t => t.Score);
        var topicTotal = topics.Values.Sum();
        var signals = signalsTask.Result;

        var result = new Dictionary<Guid, double[]>(postIds.Count);
        foreach (var post in posts)
        {
            var s = signals.GetValueOrDefault(post.Id);
            var impressionCount = s?.Impressions ?? 0;
            var engagements = likes.GetValueOrDefault(post.Id) + comments.GetValueOrDefault(post.Id);
            // How much of what the viewer engages with is on this post's topics: 0 (nothing) to 1 (all of it)
            var history = withAuthor.GetValueOrDefault(post.AuthorId);
            var topicMatch = topicTotal == 0 ? 0 : tags[post.Id].Sum(t => t.Confidence * topics.GetValueOrDefault(t.Tag)) / topicTotal;

            result[post.Id] =
            [
                Math.Max(0, (now - post.CreatedAt).TotalMinutes),
                follows.Contains(post.AuthorId) ? 1 : 0,
                affinity[post.AuthorId],
                Math.Min(1, topicMatch),
                likes.GetValueOrDefault(post.Id),
                comments.GetValueOrDefault(post.Id),
                impressionCount,
                s?.Opens ?? 0,
                ((s?.Opens ?? 0) + PriorOpens) / (impressionCount + PriorImpressions),
                Math.Min(1, (engagements + PriorEngagements) / (impressionCount + PriorImpressions)),
                s?.AverageDwellSeconds ?? 0,
                Math.Log(1 + followers.GetValueOrDefault(post.AuthorId)),
                withMedia.Contains(post.Id) ? 1 : 0,
                post.Content.Length,
                velocity.GetValueOrDefault(post.Id),
                ((history?.Opened ?? 0) + PriorOpens) / ((history?.Seen ?? 0) + PriorAuthorSeen),
                history?.Seen ?? 0,
                sessionTotal == 0 ? 0 : Math.Min(1, tags[post.Id].Sum(t => t.Confidence * sessionTopics.GetValueOrDefault(t.Tag)) / sessionTotal),
                viewer is not null && postVectors.TryGetValue(post.Id, out var vector) ? Math.Max(0, TextEmbedder.Cosine(viewer, vector)) : 0,
                // Scored as if at the top; FeedLog records the real position
                0,
            ];
        }
        return result;
    }
}
