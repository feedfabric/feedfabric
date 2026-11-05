using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Feed;

public static class FeedPostQueries
{
    // Signs fresh URLs for each post's images; done after the query since signing can't happen in SQL
    public static async Task AttachMediaAsync(this IReadOnlyList<HomeFeedPostResponse> posts, IMediaService media, CancellationToken cancellationToken)
    {
        var byPost = await media.ForPostsAsync(posts.Select(p => p.Id).ToList(), cancellationToken);
        foreach (var post in posts)
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
            Tags = db.PostTags.Where(t => t.PostId == p.Id)
                .OrderBy(t => t.Source).ThenBy(t => t.Tag)
                .Select(t => new TagResponse { Tag = t.Tag, Source = t.Source == TagSource.Hashtag ? "hashtag" : "inferred" })
                .ToList(),
        });
}
