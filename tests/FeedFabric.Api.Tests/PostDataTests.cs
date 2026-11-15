using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FeedFabric.Api.Tests;

// Posts with structured data, checked against a data type's JSON Schema. These posts have no text, so they don't need
// the moderation models.
[Collection("api")]
public class PostDataTests(ApiFactory api)
{
    private static readonly object WeightSchema = new Dictionary<string, object>
    {
        ["type"] = "object",
        ["required"] = new[] { "kg" },
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object>
        {
            ["kg"] = new { type = "number", minimum = 20, maximum = 400 },
            ["note"] = new { type = "string", maxLength = 200 },
        },
    };

    private async Task SaveWeightTypeAsync(string key)
    {
        var response = await api.ClientFor("types-admin").PutAsJsonAsync($"/admin/data-types/{key}",
            new { label = "Weight reading", description = "One weigh-in", schema = WeightSchema }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_post_with_matching_data_keeps_it_and_its_app_tags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SaveWeightTypeAsync("weight-reading");
        var client = api.ClientFor("weigher-1");

        var created = await client.PostAsJsonAsync("/userfeed/posts",
            new { tags = new[] { "weight", "Morning" }, data = new { type = "weight-reading", value = new { kg = 82.4, note = "after a run" } } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = await created.JsonAsync();
        Assert.Equal("weight-reading", post.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal(1, post.GetProperty("data").GetProperty("version").GetInt32());
        Assert.Equal(82.4, post.GetProperty("data").GetProperty("value").GetProperty("kg").GetDouble());

        var tagged = await (await client.GetAsync("/userfeed/tags/weight/posts", cancellationToken)).JsonAsync();
        var fromTag = tagged.GetProperty("posts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == post.GetProperty("id").GetString());
        Assert.Equal(82.4, fromTag.GetProperty("data").GetProperty("value").GetProperty("kg").GetDouble());
        Assert.Contains(fromTag.GetProperty("tags").EnumerateArray(), t => t.GetProperty("tag").GetString() == "morning" && t.GetProperty("source").GetString() == "app");

        var mine = await (await client.GetAsync("/userfeed?type=weight-reading", cancellationToken)).JsonAsync();
        Assert.Single(mine.GetProperty("posts").EnumerateArray());
    }

    [Fact]
    public async Task Data_that_breaks_the_schema_is_refused_with_where_and_why()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SaveWeightTypeAsync("weight-strict");
        var client = api.ClientFor("weigher-2");

        var response = await client.PostAsJsonAsync("/userfeed/posts",
            new { data = new { type = "weight-strict", value = new { kg = "eighty", colour = "blue" } } }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.JsonAsync();
        Assert.Equal("post.data_invalid", problem.GetProperty("code").GetString());
        Assert.Contains(problem.GetProperty("errors").EnumerateArray(), e => e.GetProperty("path").GetString() == "/kg");
    }

    [Fact]
    public async Task Unknown_types_and_wordless_posts_without_data_are_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = api.ClientFor("weigher-3");

        var unknown = await client.PostAsJsonAsync("/userfeed/posts", new { data = new { type = "no-such-type", value = new { } } }, cancellationToken);
        var empty = await client.PostAsJsonAsync("/userfeed/posts", new { content = "" }, cancellationToken);
        var badTag = await client.PostAsJsonAsync("/userfeed/posts",
            new { tags = new[] { "not a tag" }, data = new { type = "no-such-type", value = new { } } }, cancellationToken);

        Assert.Equal("post.data_type_unknown", await unknown.CodeAsync());
        Assert.Equal("validation_failed", await empty.CodeAsync());
        var badTagProblem = await badTag.JsonAsync();
        Assert.Contains(badTagProblem.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == "tags");
    }

    [Fact]
    public async Task A_schema_must_describe_an_object_and_a_new_schema_is_a_new_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = api.ClientFor("types-admin");

        var notObject = await admin.PutAsJsonAsync("/admin/data-types/steps", new { label = "Steps", schema = new { type = "number" } }, cancellationToken);
        var badKey = await admin.PutAsJsonAsync("/admin/data-types/Not_A_Key", new { label = "x", schema = WeightSchema }, cancellationToken);
        await SaveWeightTypeAsync("weight-versioned");
        await SaveWeightTypeAsync("weight-versioned");
        var changed = await admin.PutAsJsonAsync("/admin/data-types/weight-versioned",
            new { label = "Weight reading", schema = new { type = "object", properties = new { kg = new { type = "number" } } } }, cancellationToken);

        Assert.Equal("data_type.invalid_schema", await notObject.CodeAsync());
        Assert.Equal("data_type.invalid_key", await badKey.CodeAsync());
        Assert.Equal(2, (await changed.JsonAsync()).GetProperty("version").GetInt32());

        var schema = await api.ClientFor("any-member").GetAsync("/data-types/weight-versioned/schema", cancellationToken);
        Assert.Equal("application/schema+json", schema.Content.Headers.ContentType?.MediaType);
        Assert.Equal(JsonValueKind.Object, (await schema.JsonAsync()).GetProperty("properties").ValueKind);
    }
}
