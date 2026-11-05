using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Feed;

public static class FeedPostQueries
{
    // Not deleted, visible to everyone, and by an account that isn't suspended; for a repost, the same of its original
    public static IQueryable<PostEntity> Live(this IQueryable<PostEntity> posts, AppDbContext db) => posts.Live(db, null);

    // As Live, plus the viewer's own held posts, which only they see until a reviewer decides
    public static IQueryable<PostEntity> Live(this IQueryable<PostEntity> posts, AppDbContext db, Guid? viewerId) =>
        posts.Where(p => p.DeletedAt == null
            && (p.Status == ContentStatus.Visible || (p.Status == ContentStatus.Held && p.AuthorId == viewerId))
            && db.Users.Any(u => u.Id == p.AuthorId && u.Status == UserStatus.Active)
            && (p.RepostOfId == null || db.Posts.Any(o => o.Id == p.RepostOfId && o.DeletedAt == null
                && (o.Status == ContentStatus.Visible || (o.Status == ContentStatus.Held && o.AuthorId == viewerId))
                && db.Users.Any(u => u.Id == o.AuthorId && u.Status == UserStatus.Active))));

    // Fills in RepostOf on each repost with the post it shares
    public static async Task AttachRepostsAsync(this IReadOnlyList<HomeFeedPostResponse> posts, AppDbContext db, CancellationToken cancellationToken)
    {
        var ids = posts.Where(p => p.RepostOfId is not null).Select(p => p.RepostOfId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var originals = await db.Posts.Where(p => ids.Contains(p.Id)).ToFeedPosts(db).ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var post in posts.Where(p => p.RepostOfId is not null))
        {
            post.RepostOf = originals.GetValueOrDefault(post.RepostOfId!.Value);
        }
    }

    // Signs fresh URLs for each post's images, reposted ones included; done after the query since signing can't happen in SQL
    public static async Task AttachMediaAsync(this IReadOnlyList<HomeFeedPostResponse> posts, IMediaService media, CancellationToken cancellationToken)
    {
        var all = posts.Concat(posts.Select(p => p.RepostOf).OfType<HomeFeedPostResponse>()).ToList();
        var byPost = await media.ForPostsAsync(all.Select(p => p.Id).Distinct().ToList(), cancellationToken);
        foreach (var post in all)
        {
            post.Media = byPost.GetValueOrDefault(post.Id) ?? [];
        }
    }

    // Posts with their author and tags, for any list of posts from more than one author
    public static IQueryable<HomeFeedPostResponse> ToFeedPosts(this IQueryable<PostEntity> posts, AppDbContext db) =>
        posts.AsNoTracking().Join(db.Users, p => p.AuthorId, u => u.Id, (p, u) => new HomeFeedPostResponse
        {
            Id = p.Id,
            AuthorId = p.AuthorId,
            AuthorExternalUserId = u.ExternalUserId,
            AuthorUsername = u.Username,
            Content = p.Content,
            CreatedAt = p.CreatedAt,
            EditedAt = p.EditedAt,
            RepostOfId = p.RepostOfId,
            IsHeld = p.Status == ContentStatus.Held,
            Tags = db.PostTags.Where(t => t.PostId == p.Id)
                .OrderBy(t => t.Source).ThenBy(t => t.Tag)
                .Select(t => new TagResponse { Tag = t.Tag, Source = t.Source == TagSource.Hashtag ? "hashtag" : "inferred" })
                .ToList(),
        });
}
