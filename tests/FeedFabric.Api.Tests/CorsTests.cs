using System.Net;

namespace FeedFabric.Api.Tests;

public sealed class CorsFactory : ApiFactory
{
    public const string AdminOrigin = "https://admin.feedfabric.example";

    public CorsFactory() => Settings["Cors:AllowedOrigins:0"] = AdminOrigin;
}

public class CorsTests(CorsFactory api) : IClassFixture<CorsFactory>
{
    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/userfeed/posts");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await api.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_admin_panels_origin_may_call_the_api()
    {
        var response = await PreflightAsync(CorsFactory.AdminOrigin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(CorsFactory.AdminOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Other_origins_get_no_cors_headers()
    {
        var response = await PreflightAsync("https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
