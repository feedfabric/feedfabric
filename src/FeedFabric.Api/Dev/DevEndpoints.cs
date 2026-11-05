using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Tags;
using StackExchange.Redis;

namespace FeedFabric.Api.Dev;

// Development only: read-only views for the simulator page at /, which has no auth and is never mapped outside Development
public static class DevEndpoints
{
    private const int ListSize = 30;
    private const int PostCount = 20;
    private const int ActivitySize = 15;

    public static void MapDevEndpoints(this WebApplication app)
    {
        var dev = app.MapGroup("/dev");

        // The simulated user who posted most recently, so the page can follow whoever is active
        dev.MapGet("/active-user", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var externalUserId = await db.Posts
                .Where(p => p.DeletedAt == null)
                .OrderByDescending(p => p.Id)
                .Select(p => db.Users.Where(u => u.Id == p.AuthorId).Select(u => u.ExternalUserId).First())
                .FirstOrDefaultAsync(cancellationToken);
            return externalUserId is null ? Results.NotFound() : Results.Ok(new { externalUserId });
        });

        // Like and comment counts for a page of posts, and whether the viewer likes each one
        dev.MapGet("/likes", async (string ids, string? viewer, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var postIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).Take(100).ToList();
            var viewerId = await FindIdAsync(db, viewer, cancellationToken);
            return await db.Posts.AsNoTracking()
                .Where(p => postIds.Contains(p.Id))
                .Select(p => new
                {
                    PostId = p.Id,
                    Likes = db.Likes.Count(l => l.PostId == p.Id),
                    LikedByViewer = db.Likes.Any(l => l.PostId == p.Id && l.UserId == viewerId),
                    Comments = db.Comments.Count(c => c.PostId == p.Id && c.DeletedAt == null),
                })
                .ToListAsync(cancellationToken);
        });

        // The latest likes and comments from anyone, and the viewer's top authors and topics as the worker has scored them
        // in Redis (a like counts 1, a comment 2; topics are weighted by how sure each tag is)
        dev.MapGet("/engagement", async (string? viewer, AppDbContext db, IConnectionMultiplexer redis, CancellationToken cancellationToken) =>
        {
            var likes = await db.Likes.AsNoTracking()
                .OrderByDescending(l => l.CreatedAt).Take(ActivitySize)
                .Join(db.Users, l => l.UserId, u => u.Id, (l, actor) => new { l, actor })
                .Join(db.Posts, x => x.l.PostId, p => p.Id, (x, p) => new { x.l, x.actor, p })
                .Join(db.Users, x => x.p.AuthorId, u => u.Id, (x, author) => new Activity(
                    "like", x.l.CreatedAt, x.actor.ExternalUserId, x.actor.Username, author.ExternalUserId, author.Username, x.p.Id, x.p.Content, null))
                .ToListAsync(cancellationToken);
            var comments = await db.Comments.AsNoTracking()
                .Where(c => c.DeletedAt == null)
                .OrderByDescending(c => c.Id).Take(ActivitySize)
                .Join(db.Users, c => c.AuthorId, u => u.Id, (c, actor) => new { c, actor })
                .Join(db.Posts, x => x.c.PostId, p => p.Id, (x, p) => new { x.c, x.actor, p })
                .Join(db.Users, x => x.p.AuthorId, u => u.Id, (x, author) => new Activity(
                    x.c.ParentCommentId == null ? "comment" : "reply", x.c.CreatedAt, x.actor.ExternalUserId, x.actor.Username,
                    author.ExternalUserId, author.Username, x.p.Id, x.p.Content, x.c.Content))
                .ToListAsync(cancellationToken);
            var recent = likes.Concat(comments).OrderByDescending(a => a.CreatedAt).Take(ActivitySize).ToList();

            var viewerId = await FindIdAsync(db, viewer, cancellationToken);
            var affinity = new List<object>();
            var topics = new List<object>();
            if (viewerId is { } id)
            {
                try
                {
                    var topTopics = await redis.GetDatabase().SortedSetRangeByRankWithScoresAsync(EngagementKeys.Topics(id), 0, 5, Order.Descending);
                    topics.AddRange(topTopics.Where(t => t.Score > 0).Select(t => new { Tag = t.Element.ToString(), Score = Math.Round(t.Score, 1) }));

                    var top = await redis.GetDatabase().SortedSetRangeByRankWithScoresAsync(EngagementKeys.Affinity(id), 0, 4, Order.Descending);
                    var authorIds = top.Select(e => Guid.Parse(e.Element.ToString())).ToList();
                    var names = await db.Users.Where(u => authorIds.Contains(u.Id))
                        .ToDictionaryAsync(u => u.Id, u => new { u.ExternalUserId, u.Username }, cancellationToken);
                    affinity.AddRange(top.Zip(authorIds)
                        .Where(x => x.First.Score > 0 && names.ContainsKey(x.Second))
                        .Select(x => new { Author = names[x.Second], Score = (int)x.First.Score }));
                }
                catch (RedisException)
                {
                    // Redis down: the panel shows no affinity rather than an error
                }
            }

            return new { recent, affinity, topics };
        });

        // One post with its author and counts, for the page's post route
        dev.MapGet("/posts/{postId:guid}", async (Guid postId, string? viewer, AppDbContext db, IMediaService mediaService,
            CancellationToken cancellationToken) =>
        {
            var viewerId = await FindIdAsync(db, viewer, cancellationToken);
            var post = await db.Posts.AsNoTracking()
                .Where(p => p.Id == postId && p.DeletedAt == null)
                .Join(db.Users, p => p.AuthorId, u => u.Id, (p, u) => new
                {
                    p.Id,
                    p.Content,
                    p.CreatedAt,
                    Author = new { u.ExternalUserId, u.Username },
                    Likes = db.Likes.Count(l => l.PostId == p.Id),
                    LikedByViewer = db.Likes.Any(l => l.PostId == p.Id && l.UserId == viewerId),
                    Comments = db.Comments.Count(c => c.PostId == p.Id && c.DeletedAt == null),
                    Tags = db.PostTags.Where(t => t.PostId == p.Id)
                        .OrderBy(t => t.Source).ThenBy(t => t.Tag)
                        .Select(t => new TagResponse { Tag = t.Tag, Source = t.Source == TagSource.Hashtag ? "hashtag" : "inferred" })
                        .ToList(),
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (post is null)
            {
                return Results.NotFound();
            }
            var media = await mediaService.ForPostsAsync([post.Id], cancellationToken);
            return Results.Ok(new
            {
                post.Id, post.Content, post.CreatedAt, post.Author, post.Likes, post.LikedByViewer, post.Comments, post.Tags,
                Media = media.GetValueOrDefault(post.Id) ?? [],
            });
        });

        // @mention links carry a username; the app's routes use the external ID
        dev.MapGet("/users/by-username/{username}", async (string username, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Username == username.ToLowerInvariant())
                .Select(u => new { u.ExternalUserId, u.Username })
                .FirstOrDefaultAsync(cancellationToken);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        // Username search for the picker
        dev.MapGet("/users", async (string? q, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var users = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                users = users.Where(u => u.Username.Contains(q.Trim()) || u.ExternalUserId.Contains(q.Trim()));
            }
            return await users.OrderBy(u => u.Username).Take(ListSize)
                .Select(u => new { u.ExternalUserId, u.Username })
                .ToListAsync(cancellationToken);
        });

        // viewer is the user the page is acting as, for "do I follow them" and "did I like this"
        dev.MapGet("/users/{externalUserId}", async (string externalUserId, string? viewer, AppDbContext db, IMediaService mediaService,
            CancellationToken cancellationToken) =>
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.ExternalUserId == externalUserId)
                .Select(u => new { u.Id, u.ExternalUserId, u.Username })
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return Results.NotFound();
            }

            var viewerId = await FindIdAsync(db, viewer, cancellationToken);
            var viewerFollows = viewerId is { } v && await db.Follows.AnyAsync(f => f.FollowerId == v && f.FolloweeId == user.Id, cancellationToken);

            var followerCount = await db.Follows.CountAsync(f => f.FolloweeId == user.Id, cancellationToken);
            var followingCount = await db.Follows.CountAsync(f => f.FollowerId == user.Id, cancellationToken);
            var postCount = await db.Posts.CountAsync(p => p.AuthorId == user.Id && p.DeletedAt == null, cancellationToken);

            var followers = await db.Follows.AsNoTracking()
                .Where(f => f.FolloweeId == user.Id)
                .Join(db.Users, f => f.FollowerId, u => u.Id, (f, u) => new { u.ExternalUserId, u.Username })
                .OrderBy(u => u.Username).Take(ListSize)
                .ToListAsync(cancellationToken);
            var following = await db.Follows.AsNoTracking()
                .Where(f => f.FollowerId == user.Id)
                .Join(db.Users, f => f.FolloweeId, u => u.Id, (f, u) => new { u.ExternalUserId, u.Username })
                .OrderBy(u => u.Username).Take(ListSize)
                .ToListAsync(cancellationToken);

            var posts = await db.Posts.AsNoTracking()
                .Where(p => p.AuthorId == user.Id && p.DeletedAt == null)
                .OrderByDescending(p => p.Id).Take(PostCount)
                .Select(p => new
                {
                    p.Id,
                    p.Content,
                    p.CreatedAt,
                    Likes = db.Likes.Count(l => l.PostId == p.Id),
                    LikedByViewer = db.Likes.Any(l => l.PostId == p.Id && l.UserId == viewerId),
                    Comments = db.Comments.Count(c => c.PostId == p.Id && c.DeletedAt == null),
                    Tags = db.PostTags.Where(t => t.PostId == p.Id)
                        .OrderBy(t => t.Source).ThenBy(t => t.Tag)
                        .Select(t => new TagResponse { Tag = t.Tag, Source = t.Source == TagSource.Hashtag ? "hashtag" : "inferred" })
                        .ToList(),
                })
                .ToListAsync(cancellationToken);

            var media = await mediaService.ForPostsAsync(posts.Select(p => p.Id).ToList(), cancellationToken);
            return Results.Ok(new
            {
                user.ExternalUserId,
                user.Username,
                followerCount,
                followingCount,
                postCount,
                viewerFollows,
                followers,
                following,
                Posts = posts.Select(p => new
                {
                    p.Id, p.Content, p.CreatedAt, p.Likes, p.LikedByViewer, p.Comments, p.Tags,
                    Media = media.GetValueOrDefault(p.Id) ?? [],
                }),
            });
        });
    }

    private static async Task<Guid?> FindIdAsync(AppDbContext db, string? externalUserId, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(externalUserId)
            ? null
            : await db.Users.Where(u => u.ExternalUserId == externalUserId).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);

    // One line in the page's live activity panel: who liked or commented on whose post
    private record Activity(string Kind, DateTime CreatedAt, string ActorExternalUserId, string ActorUsername,
        string AuthorExternalUserId, string AuthorUsername, Guid PostId, string PostContent, string? CommentContent);
}
