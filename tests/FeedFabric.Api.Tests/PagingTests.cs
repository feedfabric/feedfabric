using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Posts;

namespace FeedFabric.Api.Tests;

// Following ?cursor= page by page returns every row once, in the same order as one query in Postgres. Rows go in
// directly, so these tests don't need the moderation models.
[Collection("api")]
public class PagingTests(ApiFactory api)
{
    private const int Rows = 5;
    private const int Limit = 2;

    [Fact]
    public async Task Your_own_posts_page_newest_and_oldest_first()
    {
        var client = api.ClientFor("paging-own");
        var userId = await SignInAsync(client, "paging-own");
        await AddPostsAsync(userId);

        var newest = await PageAllAsync(client, $"/userfeed?limit={Limit}", "posts");
        var oldest = await PageAllAsync(client, $"/userfeed?order=oldest&limit={Limit}", "posts");

        var expected = await IdsAsync(db => db.Posts.Where(p => p.AuthorId == userId).OrderByDescending(p => p.Id).Select(p => p.Id));
        Assert.Equal(expected, newest);
        Assert.Equal(expected.AsEnumerable().Reverse(), oldest);
    }

    [Fact]
    public async Task The_home_feed_pages_newest_first()
    {
        var client = api.ClientFor("paging-home-reader");
        var readerId = await SignInAsync(client, "paging-home-reader");
        var authorId = await SignInAsync(api.ClientFor("paging-home-author"), "paging-home-author");
        await WithDbAsync(async db =>
        {
            db.Follows.Add(new FollowEntity { FollowerId = readerId, FolloweeId = authorId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        await AddPostsAsync(authorId);

        var ids = await PageAllAsync(client, $"/userfeed/home?limit={Limit}", "posts");

        var expected = await IdsAsync(db => db.Posts.Where(p => p.AuthorId == authorId).OrderByDescending(p => p.Id).Select(p => p.Id));
        Assert.Equal(expected, ids);
    }

    [Fact]
    public async Task Comments_page_oldest_first()
    {
        var client = api.ClientFor("paging-comments");
        var userId = await SignInAsync(client, "paging-comments");
        var postId = (await AddPostsAsync(userId, 1))[0];
        await WithDbAsync(async db =>
        {
            for (var i = 0; i < Rows; i++)
            {
                db.Comments.Add(new CommentEntity { PostId = postId, AuthorId = userId, Content = $"comment {i}", CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var ids = await PageAllAsync(client, $"/userfeed/posts/{postId}/comments?limit={Limit}", "comments");

        var expected = await IdsAsync(db => db.Comments.Where(c => c.PostId == postId).OrderBy(c => c.Id).Select(c => c.Id));
        Assert.Equal(expected, ids);
    }

    [Fact]
    public async Task Mentions_page_newest_first()
    {
        var client = api.ClientFor("paging-mentioned");
        var mentionedId = await SignInAsync(client, "paging-mentioned");
        var authorId = await SignInAsync(api.ClientFor("paging-mentioner"), "paging-mentioner");
        var postIds = await AddPostsAsync(authorId);
        await WithDbAsync(async db =>
        {
            foreach (var postId in postIds)
            {
                db.Mentions.Add(new MentionEntity { MentionedUserId = mentionedId, AuthorId = authorId, PostId = postId, CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var ids = await PageAllAsync(client, $"/userfeed/mentions?limit={Limit}", "mentions");

        var expected = await IdsAsync(db => db.Mentions.Where(m => m.MentionedUserId == mentionedId).OrderByDescending(m => m.Id).Select(m => m.Id));
        Assert.Equal(expected, ids);
    }

    private async Task<Guid> SignInAsync(HttpClient client, string externalUserId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", cancellationToken)).StatusCode);
        return await IdAsync(db => db.Users.Where(u => u.ExternalUserId == externalUserId).Select(u => u.Id));
    }

    private Task<List<Guid>> AddPostsAsync(Guid authorId, int count = Rows) =>
        WithDbAsync(async db =>
        {
            var posts = Enumerable.Range(0, count)
                .Select(i => new PostEntity { AuthorId = authorId, Content = $"post {i}", CreatedAt = DateTime.UtcNow })
                .ToList();
            db.Posts.AddRange(posts);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return posts.Select(p => p.Id).ToList();
        });

    // Every id across the pages, following nextCursor until it's null
    private static async Task<List<Guid>> PageAllAsync(HttpClient client, string url, string items)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ids = new List<Guid>();
        Guid? cursor = null;
        for (var pages = 0; pages <= Rows; pages++)
        {
            var response = await client.GetAsync(cursor is null ? url : $"{url}&cursor={cursor}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.JsonAsync();
            var page = body.GetProperty(items).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
            Assert.InRange(page.Count, 1, Limit);
            ids.AddRange(page);
            var next = body.GetProperty("nextCursor");
            if (next.ValueKind == JsonValueKind.Null)
            {
                return ids;
            }
            cursor = next.GetGuid();
        }
        Assert.Fail($"{url} was still returning a cursor after {Rows + 1} pages");
        return ids;
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = api.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task WithDbAsync(Func<AppDbContext, Task> work) => WithDbAsync(async db =>
    {
        await work(db);
        return true;
    });

    private Task<Guid> IdAsync(Func<AppDbContext, IQueryable<Guid>> query) =>
        WithDbAsync(db => query(db).SingleAsync(TestContext.Current.CancellationToken));

    private Task<List<Guid>> IdsAsync(Func<AppDbContext, IQueryable<Guid>> query) =>
        WithDbAsync(db => query(db).ToListAsync(TestContext.Current.CancellationToken));
}
