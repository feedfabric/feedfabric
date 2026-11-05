using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Media;

namespace FeedFabric.Application.Services.Tags;

public class TagService(AppDbContext db, IMediaService media, TimeProvider timeProvider) : ITagService
{
    public async Task<HomeFeedResponse> PostsAsync(string tag, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var normalized = tag.TrimStart('#').ToLowerInvariant();
        var posts = db.Posts.Where(p => p.DeletedAt == null && db.PostTags.Any(t => t.PostId == p.Id && t.Tag == normalized));
        if (query.Cursor is { } cursor)
        {
            posts = posts.Where(p => p.Id.CompareTo(cursor) < 0);
        }

        // One extra row tells us whether there is another page
        var page = await posts.OrderByDescending(p => p.Id).ToFeedPosts(db).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var results = page.Take(query.Limit).ToList();
        await results.AttachMediaAsync(media, cancellationToken);
        return new HomeFeedResponse
        {
            Posts = results,
            NextCursor = page.Count > query.Limit ? results[^1].Id : null,
            Source = "postgres",
        };
    }

    public async Task<IReadOnlyList<TrendingTagResponse>> TrendingAsync(int hours, int limit, CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-hours);
        return await db.PostTags.AsNoTracking()
            .Where(t => t.CreatedAt >= since)
            .GroupBy(t => t.Tag)
            .Select(g => new TrendingTagResponse { Tag = g.Key, Posts = g.Count() })
            .OrderByDescending(t => t.Posts)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
