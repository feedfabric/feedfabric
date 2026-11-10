using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using FeedFabric.Api.Auth;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class AuthorizationTests(ApiFactory api)
{
    private static IAuthorizationService Policies(string environmentName)
    {
        IHostEnvironment environment = new HostingEnvironment { EnvironmentName = environmentName };
        var services = new ServiceCollection().AddLogging();
        services.AddAuthorizationBuilder().AddRankingPolicies(environment).AddModerationPolicies(environment).AddAdminPolicies(environment);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Member(params string[] roles) => new(new ClaimsIdentity(
        [new Claim(IdentityClaims.UserId, "someone"), .. roles.Select(role => new Claim(IdentityClaims.Role, role))],
        IdentityClaims.AuthenticationType));

    [Theory]
    [InlineData(new string[0], false, false, false)]
    [InlineData(new[] { ModerationPolicies.ReviewerRole }, true, false, true)]
    [InlineData(new[] { RankingPolicies.AdminRole }, false, true, true)]
    [InlineData(new[] { ModerationPolicies.ReviewerRole, RankingPolicies.AdminRole }, true, true, true)]
    public async Task Outside_development_each_policy_needs_its_role(string[] roles, bool reviewer, bool rankingAdmin, bool staff)
    {
        var policies = Policies(Environments.Production);
        var user = Member(roles);

        Assert.Equal(reviewer, (await policies.AuthorizeAsync(user, ModerationPolicies.Reviewer)).Succeeded);
        Assert.Equal(rankingAdmin, (await policies.AuthorizeAsync(user, RankingPolicies.Admin)).Succeeded);
        Assert.Equal(staff, (await policies.AuthorizeAsync(user, AdminPolicies.Staff)).Succeeded);
    }

    [Fact]
    public async Task A_token_cant_bring_its_own_FeedFabric_claims()
    {
        // As JwtBearer hands it over: the token's claims under their own names
        var token = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "alice"), new Claim(IdentityClaims.UserId, "bob"), new Claim(IdentityClaims.Role, ModerationPolicies.ReviewerRole)],
            "Bearer"));

        var user = await new IdentityClaims(Options.Create(new AuthOptions())).TransformAsync(token);

        Assert.Equal("alice", user.FindFirstValue(IdentityClaims.UserId));
        Assert.Single(user.FindAll(IdentityClaims.UserId));
        Assert.False(user.HasClaim(c => c.Type == IdentityClaims.Role));
        Assert.False((await Policies(Environments.Production).AuthorizeAsync(user, ModerationPolicies.Reviewer)).Succeeded);
    }

    [Fact]
    public async Task Signed_out_callers_pass_no_policy_anywhere()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        foreach (var environment in new[] { Environments.Development, Environments.Production })
        {
            var policies = Policies(environment);
            foreach (var policy in new[] { ModerationPolicies.Reviewer, RankingPolicies.Admin, AdminPolicies.Staff })
            {
                Assert.False((await policies.AuthorizeAsync(anonymous, policy)).Succeeded, $"{policy} in {environment}");
            }
        }
    }

    [Fact]
    public void Every_staff_route_has_a_staff_policy()
    {
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var staffPolicies = new[] { ModerationPolicies.Reviewer, RankingPolicies.Admin, AdminPolicies.Staff, AdminPolicies.IntegrationAdmin };
        var staffRoutes = endpoints
            .Where(e => e.RoutePattern.RawText?.TrimStart('/') is { } route && (route.StartsWith("admin") || route.StartsWith("ranking")))
            .ToList();
        var unguarded = staffRoutes
            // Any signed-in user: tells the panel what they may do
            .Where(e => e.RoutePattern.RawText?.TrimStart('/') != "admin/me")
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => staffPolicies.Contains(a.Policy)))
            .Select(e => e.DisplayName)
            .ToList();

        Assert.True(staffRoutes.Count > 20, $"Only found {staffRoutes.Count} staff routes");
        Assert.Empty(unguarded);
    }

    [Fact]
    public async Task A_suspended_member_can_read_but_not_write()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = api.ClientFor("suspended-1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me", cancellationToken)).StatusCode);
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.ExternalUserId == "suspended-1")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended), cancellationToken);
        }

        var write = await client.PostAsJsonAsync("/userfeed/posts", new { content = "hello" }, cancellationToken);
        var read = await client.GetAsync("/userfeed/home", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("user.suspended", await write.CodeAsync());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
