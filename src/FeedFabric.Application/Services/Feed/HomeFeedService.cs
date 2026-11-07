using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Feed;

// Reads the timeline fan-out wrote to Redis, then loads the posts from Postgres so text is never stale and deleted
// posts drop out. Falls back to querying Postgres when the timeline is missing, Redis is down, or a page runs past
// the newest MaxLength entries Redis keeps.
public class HomeFeedService(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IMediaService media,
    FeatureBuilder features,
    FeedLog feedLog,
    ILogger<HomeFeedService> logger) : IHomeFeedService
{
    private const string Redis = "redis";
    private const string Postgres = "postgres";

    public async Task<HomeFeedResponse?> GetHomeFeedAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }

        var feed = await ReadAsync(userId.Value, query, cancellationToken);
        // Logged with the same signals as For You, so both feeds are training data and can be compared. A repost is
        // logged as the post it shares, since that's what the viewer sees, likes and opens.
        var shown = feed.Posts.Select(p => p.RepostOf ?? p).ToList();
        var signals = await features.BuildAsync(userId.Value, shown, cancellationToken);
        await feedLog.LogServedAsync(userId.Value, FeedNames.Following, "chronological",
            shown.Select((p, i) => new ServedPost(p.Id, i, 0, signals[p.Id])).ToList(), cancellationToken);
        return feed;
    }

    public async Task<HomeFeedResponse?> PeekAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        return userId is null ? null : await ReadAsync(userId.Value, query, cancellationToken);
    }

    private async Task<HomeFeedResponse> ReadAsync(Guid userId, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        // One extra post tells us whether there is another page
        var want = query.Limit + 1;
        List<HomeFeedPostResponse> posts;
        string source;
        try
        {
            (posts, var exhausted) = await FromRedisAsync(userId, query.Cursor, want, cancellationToken);
            source = Redis;
            // Older pages continue from Postgres: Redis holds only the newest MaxLength entries, and a timeline that was
            // lost or cleared (docs/runbooks.md) is rebuilt by fan-out with new posts only
            if (exhausted && posts.Count < want)
            {
                var before = posts.Count > 0 ? posts[^1].Id : query.Cursor;
                var older = await FromPostgresAsync(userId, before, want - posts.Count, cancellationToken);
                if (older.Count > 0)
                {
                    source = posts.Count > 0 ? $"{Redis}+{Postgres}" : Postgres;
                    posts.AddRange(older);
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or TimelineMissException)
        {
            if (ex is RedisException)
            {
                logger.LogWarning(ex, "Reading the home timeline from Redis failed; using Postgres");
            }
            posts = await FromPostgresAsync(userId, query.Cursor, want, cancellationToken);
            source = Postgres;
        }

        var page = posts.Take(query.Limit).ToList();
        await page.AttachRepostsAsync(db, cancellationToken);
        await page.AttachMediaAsync(media, cancellationToken);
        return new HomeFeedResponse
        {
            Posts = page,
            NextCursor = posts.Count > query.Limit ? page[^1].Id : null,
            Source = source,
        };
    }

    // Returns up to `want` live posts after the cursor, and whether the timeline ran out before that
    private async Task<(List<HomeFeedPostResponse> Posts, bool Exhausted)> FromRedisAsync(Guid userId, Guid? cursor, int want, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var key = FeedKeys.ForUser(userId);

        var maxScore = double.PositiveInfinity;
        var cursorMember = cursor?.ToString();
        if (cursorMember is not null)
        {
            // A cursor that isn't in the timeline (trimmed, or from a Postgres page) continues from Postgres
            maxScore = await database.SortedSetScoreAsync(key, cursorMember) ?? throw new TimelineMissException();
        }
        else if (!await database.KeyExistsAsync(key))
        {
            throw new TimelineMissException();
        }

        // Read in batches, since some IDs may be deleted posts that get dropped
        var batchSize = Math.Max(want * 2, 20);
        var posts = new List<HomeFeedPostResponse>(want);
        var skip = 0L;
        while (posts.Count < want)
        {
            var entries = await database.SortedSetRangeByScoreWithScoresAsync(
                key, double.NegativeInfinity, maxScore, Exclude.None, Order.Descending, skip, batchSize);
            skip += entries.Length;

            // Same-score members come back in descending member order, and member IDs are time-ordered UUIDv7s
            var ids = entries
                .Where(e => cursorMember is null || e.Score < maxScore || string.CompareOrdinal(e.Element, cursorMember) < 0)
                .Select(e => Guid.Parse(e.Element.ToString()))
                .ToList();
            posts.AddRange(await LoadAsync(userId, ids, cancellationToken));

            if (entries.Length < batchSize)
            {
                return (posts, true);
            }
        }
        return (posts, false);
    }

    // Posts in the given order, skipping any that are deleted, missing or held (unless they're the viewer's own), and
    // reposts of those
    private async Task<List<HomeFeedPostResponse>> LoadAsync(Guid userId, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await db.Posts.Where(p => ids.Contains(p.Id)).Live(db, userId).ToFeedPosts(db)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        return ids.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    // Fan-out on read: the same feed straight from the follow graph
    private Task<List<HomeFeedPostResponse>> FromPostgresAsync(Guid userId, Guid? before, int take, CancellationToken cancellationToken)
    {
        var posts = db.Posts.Live(db, userId)
            .Where(p => p.AuthorId == userId || db.Follows.Any(f => f.FollowerId == userId && f.FolloweeId == p.AuthorId));
        if (before is { } cursor)
        {
            posts = posts.Where(p => p.Id < cursor);
        }

        return posts.OrderByDescending(p => p.Id).ToFeedPosts(db).Take(take).ToListAsync(cancellationToken);
    }

    // No cached timeline to read from, so Postgres serves the page
    private sealed class TimelineMissException : Exception;
}
