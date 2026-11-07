using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class AccountTests(ApiFactory api)
{
    // Posts go in directly, so these tests don't need the moderation models
    private async Task<Guid> SignInWithPostAsync(HttpClient client, string externalUserId, string content)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", cancellationToken)).StatusCode);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.ExternalUserId == externalUserId, cancellationToken);
        db.Posts.Add(new PostEntity { AuthorId = user.Id, Content = content, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    [Fact]
    public async Task Export_has_the_members_own_posts_and_feed_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = api.ClientFor("export-1");
        await SignInWithPostAsync(client, "export-1", "my own words");

        var response = await client.GetAsync("/me/export", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.ToString());
        using var export = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = export.RootElement;
        Assert.Equal("export-1", root.GetProperty("user").GetProperty("externalUserId").GetString());
        Assert.Equal("my own words", root.GetProperty("posts")[0].GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("feedLog").ValueKind);
    }

    [Fact]
    public async Task Deleting_empties_posts_and_profile_and_a_new_sign_in_is_a_new_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = api.ClientFor("delete-1");
        var userId = await SignInWithPostAsync(client, "delete-1", "something personal");

        var response = await client.DeleteAsync("/me", cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
            Assert.Equal(UserStatus.Deleted, user.Status);
            Assert.Equal(AccountService.DeletedExternalUserId(userId), user.ExternalUserId);
            Assert.Null(user.DisplayName);
            var post = await db.Posts.SingleAsync(p => p.AuthorId == userId, cancellationToken);
            Assert.Equal("", post.Content);
            Assert.NotNull(post.DeletedAt);
            Assert.True(await db.OutboxMessages.AnyAsync(m => m.Type == EventTypes.UserDeleted && m.PartitionKey == userId.ToString(),
                cancellationToken));
        }

        // The same token again: a fresh account with nothing in it
        var export = await client.GetAsync("/me/export", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using var document = JsonDocument.Parse(await export.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("posts").GetArrayLength());
    }

    [Fact]
    public async Task A_suspended_member_can_still_delete_their_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = api.ClientFor("delete-2");
        var userId = await SignInWithPostAsync(client, "delete-2", "hello");
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended), cancellationToken);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await client.DeleteAsync("/me", cancellationToken)).StatusCode);
    }
}
