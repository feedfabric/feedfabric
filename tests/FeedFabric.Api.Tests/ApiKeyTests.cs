using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using FeedFabric.Api.Auth;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class ApiKeyTests(ApiFactory api)
{
    private async Task<(string Id, string Key)> CreateKeyAsync(string name)
    {
        var response = await api.ClientFor("key-admin").PostAsJsonAsync("/admin/api-keys", new { name }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.JsonAsync();
        return (body.GetProperty("id").GetString()!, body.GetProperty("key").GetString()!);
    }

    private HttpClient KeyClient(string key, string? user)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.UserHeader, user);
        }
        return client;
    }

    [Fact]
    public async Task A_key_acts_for_the_member_it_names_and_creates_them_from_the_profile_headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, key) = await CreateKeyAsync("Web backend");
        var client = KeyClient(key, "sdk-member-1");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.UsernameHeader, "sdk_member");
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.DisplayNameHeader, Uri.EscapeDataString("Zoë Brontë"));

        var me = await client.GetAsync("/me", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("sdk-member-1", (await me.JsonAsync()).GetProperty("externalUserId").GetString());
        using var scope = api.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.ExternalUserId == "sdk-member-1", cancellationToken);
        Assert.Equal("sdk_member", user.Username);
        Assert.Equal("Zoë Brontë", user.DisplayName);
    }

    [Fact]
    public async Task A_key_without_a_member_says_what_is_missing()
    {
        var (_, key) = await CreateKeyAsync("No member");

        var response = await KeyClient(key, null).GetAsync("/userfeed/home", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("api_key.user_required", await response.CodeAsync());
    }

    [Fact]
    public async Task Unknown_and_revoked_keys_are_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (id, key) = await CreateKeyAsync("Soon revoked");
        var client = KeyClient(key, "sdk-member-2");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", cancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await api.ClientFor("key-admin").DeleteAsync($"/admin/api-keys/{id}", cancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await KeyClient("ffk_not-a-real-key", "sdk-member-2").GetAsync("/me", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task The_key_is_only_shown_once_and_the_list_keeps_its_prefix()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (id, key) = await CreateKeyAsync("Listed");

        var list = await (await api.ClientFor("key-admin").GetAsync("/admin/api-keys", cancellationToken)).JsonAsync();

        var row = list.EnumerateArray().Single(k => k.GetProperty("id").GetString() == id);
        Assert.StartsWith("ffk_", key);
        Assert.Equal(key[..12], row.GetProperty("prefix").GetString());
        Assert.False(row.TryGetProperty("key", out _));
        Assert.False(row.TryGetProperty("hash", out _));
    }

    [Fact]
    public async Task A_key_never_reaches_staff_routes_even_in_development()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, key) = await CreateKeyAsync("Not staff");
        var client = KeyClient(key, "sdk-member-3");

        foreach (var path in new[] { "/admin/users", "/admin/moderation/queue", "/ranking/profiles", "/admin/api-keys", "/admin/system" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path, cancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task Outside_development_making_keys_needs_its_own_role()
    {
        IHostEnvironment environment = new HostingEnvironment { EnvironmentName = Environments.Production };
        var services = new ServiceCollection().AddLogging();
        services.AddAuthorizationBuilder().AddRankingPolicies(environment).AddModerationPolicies(environment).AddAdminPolicies(environment);
        var policies = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal With(params string[] roles) => new(new ClaimsIdentity(
            [new Claim(IdentityClaims.UserId, "someone"), .. roles.Select(role => new Claim(IdentityClaims.Role, role))], IdentityClaims.AuthenticationType));

        Assert.False((await policies.AuthorizeAsync(With(ModerationPolicies.ReviewerRole, RankingPolicies.AdminRole), AdminPolicies.IntegrationAdmin)).Succeeded);
        Assert.True((await policies.AuthorizeAsync(With(AdminPolicies.IntegrationAdminRole), AdminPolicies.IntegrationAdmin)).Succeeded);
    }
}
