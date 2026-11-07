using System.Net;

namespace FeedFabric.Api.Tests;

// Redis pointed at a port nothing listens on
public sealed class RedisDownFactory : ApiFactory
{
    public RedisDownFactory() => Settings["ConnectionStrings:Redis"] = "127.0.0.1:1,connectTimeout=500";
}

public class RedisDownTests(RedisDownFactory api) : IClassFixture<RedisDownFactory>
{
    [Fact]
    public async Task The_home_feed_still_serves_from_postgres()
    {
        var response = await api.ClientFor("redis-down-1").GetAsync("/userfeed/home", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("postgres", (await response.JsonAsync()).GetProperty("source").GetString());
    }

    [Fact]
    public async Task What_needs_redis_is_503_with_a_code_and_Retry_After()
    {
        var response = await api.ClientFor("redis-down-2").GetAsync("/userfeed/foryou", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("service_unavailable", await response.CodeAsync());
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Readiness_is_degraded_not_down()
    {
        var response = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var checks = (await response.JsonAsync()).GetProperty("checks");

        Assert.Equal("Degraded", checks.GetProperty("redis").GetProperty("status").GetString());
    }
}
