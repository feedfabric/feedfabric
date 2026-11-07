using System.Net;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class DashboardTests(ApiFactory api)
{
    [Fact]
    public async Task Repeat_calls_for_the_same_window_are_served_from_the_cache()
    {
        // Development: any signed-in user is staff
        var client = api.ClientFor("dashboard-1");

        var first = await client.GetAsync("/admin/dashboard?minutes=30", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("/admin/dashboard?minutes=30", TestContext.Current.CancellationToken);
        var other = await client.GetAsync("/admin/dashboard?minutes=31", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var generatedAt = (await first.JsonAsync()).GetProperty("generatedAt").GetString();
        Assert.Equal(generatedAt, (await second.JsonAsync()).GetProperty("generatedAt").GetString());
        Assert.NotEqual(generatedAt, (await other.JsonAsync()).GetProperty("generatedAt").GetString());
    }
}
