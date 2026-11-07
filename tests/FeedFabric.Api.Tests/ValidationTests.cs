using System.Net;
using System.Net.Http.Json;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Posts;

namespace FeedFabric.Api.Tests;

[Collection("api")]
public class ValidationTests(ApiFactory api)
{
    [Fact]
    public async Task Post_longer_than_the_cap_is_too_long()
    {
        var client = api.ClientFor("validation-1");
        var response = await client.PostAsJsonAsync("/userfeed/posts",
            new { content = new string('a', PostRequest.MaxContentLength + 1) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal("content", error.GetProperty("field").GetString());
        Assert.Equal("too_long", error.GetProperty("code").GetString());
        Assert.Equal(PostRequest.MaxContentLength, error.GetProperty("params").GetProperty("max").GetInt32());
    }

    [Fact]
    public async Task Comment_longer_than_the_cap_is_too_long()
    {
        var client = api.ClientFor("validation-2");
        var response = await client.PostAsJsonAsync($"/userfeed/posts/{Guid.NewGuid()}/comments",
            new { content = new string('a', CommentRequest.MaxContentLength + 1) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single((await response.JsonAsync()).GetProperty("errors").EnumerateArray());
        Assert.Equal("too_long", error.GetProperty("code").GetString());
        Assert.Equal(CommentRequest.MaxContentLength, error.GetProperty("params").GetProperty("max").GetInt32());
    }

    [Fact]
    public async Task Body_over_the_request_limit_is_refused()
    {
        var client = api.ClientFor("validation-3");
        // Over Kestrel:Limits:MaxRequestBodySize (1 MB)
        using var content = new StringContent($$"""{"content":"{{new string('a', 1_100_000)}}"}""", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/userfeed/posts", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request_too_large", await response.CodeAsync());
    }
}
