using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FeedFabric.Sdk.Tests;

// A stand-in API: records each request and answers with whatever the test queued
public sealed class FakeApi : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, HttpRequestMessage Request, string Body)> Seen { get; } = [];

    private readonly Queue<(int Status, object? Body, string? RetryAfter)> replies = new();

    public void Reply(int status, object? body = null, string? retryAfter = null) => replies.Enqueue((status, body, retryAfter));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Seen.Add((request.Method, request.RequestUri!.PathAndQuery, request, body));
        var (status, reply, retryAfter) = replies.Count > 0 ? replies.Dequeue() : (204, null, null);
        var response = new HttpResponseMessage((HttpStatusCode)status);
        if (reply is not null)
        {
            response.Content = new StringContent(JsonSerializer.Serialize(reply), Encoding.UTF8, "application/json");
        }
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }
        return response;
    }

    public FeedFabricClient Client() => new("http://feed.test", "ffk_test", new HttpClient(this)) { UploadPoll = TimeSpan.FromMilliseconds(10) };
}

public record WeightReading(double Kg, string? Note = null);

public class ClientTests
{
    private static readonly DataType<WeightReading> Weight = new("weight-reading");

    private static readonly object Saved = new
    {
        id = "0199a1b2-0000-7000-8000-000000000001", authorId = "0199a1b2-0000-7000-8000-000000000002", content = "",
        createdAt = "2026-11-10T09:00:00Z", isHeld = false,
        data = new { type = "weight-reading", version = 2, value = new { kg = 82.4 } },
    };

    private readonly FakeApi api = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActsForTheMemberWithTheKeyAndEncodedProfile()
    {
        api.Reply(200, new { externalUserId = "user 1" });

        var me = await api.Client().AsMember("user 1", new MemberProfile(DisplayName: "Zoë Brontë")).MeAsync(Ct);

        Assert.Equal("user 1", me.ExternalUserId);
        var (method, path, request, _) = api.Seen[0];
        Assert.Equal((HttpMethod.Get, "/me"), (method, path));
        Assert.Equal("Bearer ffk_test", request.Headers.Authorization?.ToString());
        Assert.Equal("user%201", request.Headers.GetValues("FeedFabric-User").Single());
        Assert.Equal("Zoë Brontë", Uri.UnescapeDataString(request.Headers.GetValues("FeedFabric-Display-Name").Single()));
        Assert.False(request.Headers.Contains("FeedFabric-Username"));
    }

    [Fact]
    public async Task PostsTypedDataAndAppTagsAndReadsItBack()
    {
        api.Reply(201, Saved);

        var post = await api.Client().AsMember("u1").Posts.CreateAsync(
            new PostInput { Tags = ["weight"], Data = Weight.Of(new WeightReading(82.4)) }, Ct);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"tags":["weight"],"data":{"type":"weight-reading","value":{"kg":82.4}}}"""),
            JsonNode.Parse(api.Seen[0].Body)), api.Seen[0].Body);
        Assert.True(Weight.TryRead(post.Data, out var reading));
        Assert.Equal(new WeightReading(82.4), reading);
        Assert.Null(new DataType<Dictionary<string, int>>("steps").Read(post.Data));
        Assert.False(Weight.Is(null));
    }

    [Fact]
    public void ReadThrowsForDataThatIsNotTheType()
    {
        var data = new PostData("weight-reading", 1, JsonDocument.Parse("""{"kg":"heavy"}""").RootElement);

        Assert.ThrowsAny<JsonException>(() => Weight.Read(data));
    }

    [Fact]
    public async Task BuildsQueryStringsAndLeavesOutWhatIsUnset()
    {
        api.Reply(200, new { posts = Array.Empty<object>(), nextCursor = (string?)null });

        await api.Client().AsMember("u1").Posts.ListAsync(new OwnPostsQuery { Type = "weight-reading", Order = PostOrder.Oldest, Limit = 5 }, Ct);

        Assert.Equal("/userfeed?type=weight-reading&order=oldest&limit=5", api.Seen[0].Path);
    }

    [Fact]
    public async Task ErrorsCarryTheCodeParamsAndFieldErrors()
    {
        api.Reply(400, new
        {
            code = "post.data_invalid", detail = "nope", @params = new { type = "weight-reading" },
            errors = new[] { new { path = "/kg", message = "should be a number" } }, traceId = "t-1",
        });

        var error = await Assert.ThrowsAsync<FeedFabricException>(() =>
            api.Client().AsMember("u1").Posts.CreateAsync(new PostInput { Data = Weight.Of(new WeightReading(1)) }, Ct));

        Assert.Equal((400, "post.data_invalid", "t-1"), (error.Status, error.Code, error.TraceId));
        Assert.Equal("weight-reading", error.Params["type"].GetString());
        Assert.Equal("/kg", error.Errors[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task WaitsOutA429EvenForAPost()
    {
        api.Reply(429, new { code = "rate_limited" }, retryAfter: "0");
        api.Reply(202, new { counted = 2 });

        var counted = await api.Client().AsMember("u1").Signals.ImpressionsAsync([Guid.NewGuid(), Guid.NewGuid()], Ct);

        Assert.Equal(2, counted.Counted);
        Assert.Equal(2, api.Seen.Count);
    }

    [Fact]
    public async Task RepeatsAGetAfterA503ButNotAPost()
    {
        var member = api.Client().AsMember("u1");
        api.Reply(503, new { code = "service_unavailable" }, retryAfter: "0");
        await Assert.ThrowsAsync<FeedFabricException>(() => member.Posts.CreateAsync(new PostInput { Content = "hi" }, Ct));
        Assert.Single(api.Seen);

        api.Reply(503, new { code = "service_unavailable" }, retryAfter: "0");
        api.Reply(200, Array.Empty<object>());
        Assert.Empty(await member.FavouritesAsync(Ct));
        Assert.Equal(3, api.Seen.Count);
    }

    [Fact]
    public async Task UploadsAnImageAndWaitsUntilItIsReady()
    {
        var id = Guid.NewGuid();
        api.Reply(201, new { mediaId = id, uploadUrl = $"http://blobs.test/{id}", contentType = "image/png", expiresAt = "2026-11-10T10:00:00Z" });
        api.Reply(200);
        api.Reply(202, new { id, status = "processing" });
        api.Reply(200, new { id, status = "ready", url = "u", thumbnailUrl = "t", width = 1, height = 1 });

        var media = await api.Client().AsMember("u1").Media.UploadAsync([1, 2, 3], "image/png", Ct);

        Assert.Equal("ready", media.Status);
        Assert.Equal([$"POST /media/uploads", $"PUT /{id}", $"POST /media/{id}/complete", $"GET /media/{id}"],
            api.Seen.Select(s => $"{s.Method} {s.Path}"));
        Assert.Null(api.Seen[1].Request.Headers.Authorization);
    }

    [Fact]
    public void RefusesAWrongLookingKey() =>
        Assert.Throws<ArgumentException>(() => new FeedFabricClient("http://localhost", "eyJhbGciOi..."));
}
