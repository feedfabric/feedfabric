using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Admin;

// Posts as staff see them: every status, with counts, comments, reports and moderation history. Approving and
// removing go through ReviewService, so they land in the log like any other decision.
public class AdminPostService(AppDbContext db, IMediaService media)
{
    public async Task<AdminPage<AdminPostRow>> SearchAsync(string? q, string? author, string? status, bool reportedOnly, int offset, int limit,
        CancellationToken cancellationToken)
    {
        // Reposts carry no content of their own; the post they share is listed instead
        var posts = db.Posts.AsNoTracking().Where(p => p.RepostOfId == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            posts = posts.Where(p => EF.Functions.ILike(p.Content, "%" + q.Trim() + "%"));
        }
        if (!string.IsNullOrWhiteSpace(author))
        {
            var name = author.Trim().TrimStart('@');
            posts = posts.Where(p => db.Users.Any(u => u.Id == p.AuthorId && (u.Username == name.ToLower() || u.ExternalUserId == name)));
        }
        posts = status switch
        {
            "visible" => posts.Where(p => p.DeletedAt == null && p.Status == ContentStatus.Visible),
            "held" => posts.Where(p => p.DeletedAt == null && p.Status == ContentStatus.Held),
            "removed" => posts.Where(p => p.DeletedAt == null && p.Status == ContentStatus.Removed),
            "deleted" => posts.Where(p => p.DeletedAt != null),
            _ => posts,
        };
        if (reportedOnly)
        {
            posts = posts.Where(p => db.Reports.Any(r => r.PostId == p.Id && r.CommentId == null));
        }

        var total = await posts.CountAsync(cancellationToken);
        var page = await RowsAsync(posts.OrderByDescending(p => p.Id).Skip(offset).Take(limit), cancellationToken);
        return new AdminPage<AdminPostRow>(page, total);
    }

    public async Task<AdminPostDetail?> DetailAsync(Guid postId, ReviewService reviews, CancellationToken cancellationToken)
    {
        var post = (await RowsAsync(db.Posts.Where(p => p.Id == postId && p.RepostOfId == null), cancellationToken, full: true)).SingleOrDefault();
        if (post is null)
        {
            return null;
        }

        var tags = await db.PostTags.AsNoTracking().Where(t => t.PostId == postId)
            .OrderBy(t => t.Source).ThenBy(t => t.Tag)
            .Select(t => new TagResponse { Tag = t.Tag, Source = t.Source == TagSource.Hashtag ? "hashtag" : "inferred" })
            .ToListAsync(cancellationToken);
        var images = await media.ForPostsAsync([postId], cancellationToken);
        var comments = await CommentRows(db, db.Comments.Where(c => c.PostId == postId).OrderBy(c => c.Id)).ToListAsync(cancellationToken);
        return new AdminPostDetail(post, tags, images.GetValueOrDefault(postId) ?? [], comments,
            await reviews.ReportsAsync(50, cancellationToken, postId: postId),
            await reviews.LogAsync(null, 50, cancellationToken, postId: postId));
    }

    internal async Task<List<AdminPostRow>> RowsAsync(IQueryable<PostEntity> posts, CancellationToken cancellationToken, bool full = false)
    {
        var rows = await posts.AsNoTracking()
            .Join(db.Users, p => p.AuthorId, u => u.Id, (p, u) => new
            {
                p.Id, u.ExternalUserId, u.Username, p.Content, p.Status, p.DeletedAt, p.CreatedAt, p.EditedAt,
                Likes = db.Likes.Count(l => l.PostId == p.Id),
                Comments = db.Comments.Count(c => c.PostId == p.Id && c.DeletedAt == null),
                Reposts = db.Posts.Count(r => r.RepostOfId == p.Id && r.DeletedAt == null),
                Reports = db.Reports.Count(r => r.PostId == p.Id && r.CommentId == null),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AdminPostRow(r.Id, new UserRef(r.ExternalUserId, r.Username), full ? r.Content : AdminStatus.Excerpt(r.Content),
            AdminStatus.Of(r.Status, r.DeletedAt), r.CreatedAt, r.EditedAt, r.Likes, r.Comments, r.Reposts, r.Reports)).ToList();
    }

    // Callers only filter and order the comments passed in, never these rows, so AdminStatus.Of can run in the final projection
    internal static IQueryable<AdminCommentRow> CommentRows(AppDbContext db, IQueryable<CommentEntity> comments) =>
        comments.AsNoTracking().Join(db.Users, c => c.AuthorId, u => u.Id, (c, u) => new AdminCommentRow(
            c.Id, c.PostId, c.ParentCommentId, new UserRef(u.ExternalUserId, u.Username), c.Content,
            AdminStatus.Of(c.Status, c.DeletedAt), c.CreatedAt, db.Reports.Count(r => r.CommentId == c.Id)));
}
