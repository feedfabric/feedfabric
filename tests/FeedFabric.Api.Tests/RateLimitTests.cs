using System.Net;

namespace FeedFabric.Api.Tests;

public sealed class TightRateLimitsFactory : ApiFactory
{
    public TightRateLimitsFactory()
    {
        Settings["RateLimits:Reactions:PermitLimit"] = "3";
        Settings["RateLimits:Reactions:Window"] = "00:10:00";
    }
}

public class RateLimitTests(TightRateLimitsFactory api) : IClassFixture<TightRateLimitsFactory>
{
    [Fact]
    public async Task Over_the_limit_is_429_with_a_code_and_Retry_After()
    {
        var client = api.ClientFor("limits-1");
        var post = $"/userfeed/posts/{Guid.NewGuid()}/like";
        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.PutAsync(post, null, TestContext.Current.CancellationToken);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var refused = await client.PutAsync(post, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("rate_limited", await refused.CodeAsync());
        Assert.NotNull(refused.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Each_user_has_their_own_limit()
    {
        var first = api.ClientFor("limits-2");
        var second = api.ClientFor("limits-3");
        var post = $"/userfeed/posts/{Guid.NewGuid()}/like";
        for (var i = 0; i < 4; i++)
        {
            await first.PutAsync(post, null, TestContext.Current.CancellationToken);
        }

        var other = await second.PutAsync(post, null, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }
}
