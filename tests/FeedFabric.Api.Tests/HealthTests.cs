using System.Net;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class HealthTests(ApiFactory api)
{
    [Fact]
    public async Task Live_checks_nothing_and_needs_no_sign_in()
    {
        var response = await api.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.JsonAsync()).GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task Ready_checks_postgres_redis_and_the_models()
    {
        var response = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var checks = (await response.JsonAsync()).GetProperty("checks");

        Assert.Equal("Healthy", checks.GetProperty("postgres").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("redis").GetProperty("status").GetString());
        // Without the toxicity model nothing can be posted, so the API isn't ready
        var models = checks.GetProperty("models").GetProperty("status").GetString();
        Assert.Equal(ApiFactory.ModelsPresent ? "Healthy" : "Unhealthy", models);
        Assert.Equal(ApiFactory.ModelsPresent ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
