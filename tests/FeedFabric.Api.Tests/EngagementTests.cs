using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Api.Tests;

// Feed posts carry their likes, comments and reposts, and whether the signed-in user liked or reposted them: the
// member on their own feeds, the staff member on /admin/feed, since that's whose like the admin panel's button adds
[Collection("api")]
public class EngagementTests(ApiFactory api)
{
    [Fact]
    public async Task Feed_posts_carry_counts_and_the_signed_in_users_likes_and_reposts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = api.ClientFor("engagement-reader");
        var readerId = await SignInAsync(reader, "engagement-reader");
        var authorId = await SignInAsync(api.ClientFor("engagement-author"), "engagement-author");
        var fan = api.ClientFor("engagement-fan");
        await SignInAsync(fan, "engagement-fan");
        var staff = api.ClientFor("engagement-staff");
        await SignInAsync(staff, "engagement-staff");

        var postId = await WithDbAsync(async db =>
        {
            db.Follows.Add(new FollowEntity { FollowerId = readerId, FolloweeId = authorId, CreatedAt = DateTime.UtcNow });
            var post = new PostEntity { AuthorId = authorId, Content = "a post to like", CreatedAt = DateTime.UtcNow };
            db.Posts.Add(post);
            await db.SaveChangesAsync(cancellationToken);
            // One visible comment counts; someone else's held one and a deleted one don't
            db.Comments.AddRange(
                new CommentEntity { PostId = post.Id, AuthorId = authorId, Content = "visible", CreatedAt = DateTime.UtcNow },
                new CommentEntity { PostId = post.Id, AuthorId = authorId, Content = "held", CreatedAt = DateTime.UtcNow, Status = ContentStatus.Held },
                new CommentEntity { PostId = post.Id, AuthorId = authorId, Content = "deleted", CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
            return post.Id;
        });

        Assert.Equal(HttpStatusCode.NoContent, (await fan.PutAsync($"/userfeed/posts/{postId}/like", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reader.PutAsync($"/userfeed/posts/{postId}/like", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reader.PutAsync($"/userfeed/posts/{postId}/repost", null, cancellationToken)).StatusCode);

        // The reader's feed has the post, and their own repost of it: both show the original's numbers
        var mine = await ShownAsync(reader, "/userfeed/home?limit=20", postId);
        Assert.NotEmpty(mine);
        Assert.All(mine, post =>
        {
            Assert.Equal(2, post.GetProperty("likes").GetInt32());
            Assert.Equal(1, post.GetProperty("comments").GetInt32());
            Assert.Equal(1, post.GetProperty("reposts").GetInt32());
            Assert.True(post.GetProperty("liked").GetBoolean());
            Assert.True(post.GetProperty("reposted").GetBoolean());
        });

        // Staff looking at the reader's feed see the same counts, but their own likes and reposts
        var peeked = await ShownAsync(staff, "/admin/feed/engagement-reader/following?limit=20", postId);
        Assert.NotEmpty(peeked);
        Assert.All(peeked, post =>
        {
            Assert.Equal(2, post.GetProperty("likes").GetInt32());
            Assert.False(post.GetProperty("liked").GetBoolean());
            Assert.False(post.GetProperty("reposted").GetBoolean());
        });

        // Undoing both shows straight away
        Assert.Equal(HttpStatusCode.NoContent, (await reader.DeleteAsync($"/userfeed/posts/{postId}/like", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reader.DeleteAsync($"/userfeed/posts/{postId}/repost", cancellationToken)).StatusCode);
        var undone = Assert.Single(await ShownAsync(reader, "/userfeed/home?limit=20", postId));
        Assert.Equal(1, undone.GetProperty("likes").GetInt32());
        Assert.Equal(0, undone.GetProperty("reposts").GetInt32());
        Assert.False(undone.GetProperty("liked").GetBoolean());
        Assert.False(undone.GetProperty("reposted").GetBoolean());
    }

    // The posts on the first page that show postId, as the post itself or as what a repost shares
    private static async Task<List<JsonElement>> ShownAsync(HttpClient client, string url, Guid postId)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        return
        [
            .. body.GetProperty("posts").EnumerateArray()
                .Select(p => p.TryGetProperty("repostOf", out var original) && original.ValueKind == JsonValueKind.Object ? original : p)
                .Where(p => p.GetProperty("id").GetGuid() == postId),
        ];
    }

    private async Task<Guid> SignInAsync(HttpClient client, string externalUserId)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", TestContext.Current.CancellationToken)).StatusCode);
        return await WithDbAsync(db => Task.FromResult(db.Users.Single(u => u.ExternalUserId == externalUserId).Id));
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = api.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
