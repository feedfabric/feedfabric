using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Feed;

public class FeedService(AppDbContext db) : IFeedService
{
    public async Task<FeedResponse?> GetFeedAsync(string externalUserId, FeedQuery query, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }

        var posts = db.Posts.AsNoTracking()
            .Where(p => p.AuthorId == userId.Value)
            .Live(db, userId);

        if (query.Gt is { } gt)
        {
            posts = posts.Where(p => p.Id > gt);
        }
        if (query.Gte is { } gte)
        {
            posts = posts.Where(p => p.Id >= gte);
        }
        if (query.Lt is { } lt)
        {
            posts = posts.Where(p => p.Id < lt);
        }
        if (query.Lte is { } lte)
        {
            posts = posts.Where(p => p.Id <= lte);
        }

        var newestFirst = query.Order == FeedOrder.Newest;
        if (query.Cursor is { } cursor)
        {
            posts = newestFirst
                ? posts.Where(p => p.Id < cursor)
                : posts.Where(p => p.Id > cursor);
        }

        posts = newestFirst ? posts.OrderByDescending(p => p.Id) : posts.OrderBy(p => p.Id);

        // One extra row tells us whether there is another page
        var page = await posts.Take(query.Limit + 1).ToListAsync(cancellationToken);
        var results = page.Take(query.Limit).Select(PostResponse.From).ToList();

        return new FeedResponse
        {
            Posts = results,
            NextCursor = page.Count > query.Limit ? results[^1].Id : null,
        };
    }
}
