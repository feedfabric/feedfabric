using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Mentions;

public class MentionService(AppDbContext db) : IMentionService
{
    public async Task<MentionPageResponse?> ListAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }

        var mentions = db.Mentions.AsNoTracking().Where(m => m.MentionedUserId == userId);
        if (query.Cursor is { } cursor)
        {
            mentions = mentions.Where(m => m.Id < cursor);
        }

        var page = await mentions
            .OrderByDescending(m => m.Id)
            // Not from suspended accounts, whether they wrote the post or the comment that mentions you
            .Where(m => db.Users.Any(u => u.Id == m.AuthorId && u.Status == UserStatus.Active))
            .Join(db.Posts.Where(p => p.DeletedAt == null && p.Status == ContentStatus.Visible), m => m.PostId, p => p.Id, (m, p) => new { m, p })
            .Join(db.Users, x => x.m.AuthorId, u => u.Id, (x, u) => new { x.m, x.p, u })
            .Select(x => new
            {
                x.m.Id,
                x.m.CreatedAt,
                x.u.ExternalUserId,
                x.u.Username,
                x.m.PostId,
                x.m.CommentId,
                PostContent = x.p.Content,
                Comment = db.Comments.Where(c => c.Id == x.m.CommentId).Select(c => new { c.Content, c.DeletedAt, c.Status }).FirstOrDefault(),
            })
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        // Read one extra row before filtering, so a deleted comment never hides that there's another page
        var hasMore = page.Count > query.Limit;
        var results = page.Take(query.Limit)
            .Where(x => x.CommentId is null || x.Comment is { DeletedAt: null, Status: ContentStatus.Visible })
            .Select(x => new MentionResponse
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                AuthorExternalUserId = x.ExternalUserId,
                AuthorUsername = x.Username,
                PostId = x.PostId,
                CommentId = x.CommentId,
                Content = x.Comment?.Content ?? x.PostContent,
            })
            .ToList();

        return new MentionPageResponse
        {
            Mentions = results,
            NextCursor = hasMore ? page[query.Limit - 1].Id : null,
        };
    }
}
