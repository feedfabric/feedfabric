# FeedFabric for .NET

Call FeedFabric from your servers, for your members, with an API key. It targets .NET 10 and has no dependencies.
[../README.md](../README.md) covers API keys, tags and typed data for every SDK.

```sh
dotnet add package FeedFabric.Sdk
```

```csharp
using FeedFabric.Sdk;

var feed = new FeedFabricClient("https://feed.example.com", builder.Configuration["FeedFabric:ApiKey"]!);
var member = feed.AsMember(user.Id, new MemberProfile(user.Handle, user.Name));

var page = await member.Feeds.ForYouAsync(limit: 20);
await member.Posts.LikeAsync(page.Posts[0].Id);
```

`FeedFabricClient` is safe to share, so register it as a singleton. To use `IHttpClientFactory`, pass it a named
client:

```csharp
builder.Services.AddHttpClient("feedfabric");
builder.Services.AddSingleton(sp => new FeedFabricClient(
    new FeedFabricOptions { BaseUrl = new Uri("https://feed.example.com"), ApiKey = builder.Configuration["FeedFabric:ApiKey"]! },
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("feedfabric")));
```

## Typed data

```csharp
public record WeightReading(double Kg, string? Note = null);

static readonly DataType<WeightReading> Weight = new("weight-reading");

await member.Posts.CreateAsync(new PostInput { Tags = ["weight"], Data = Weight.Of(new WeightReading(82.4)) });

var page = await member.Posts.ListAsync(new OwnPostsQuery { Type = Weight.Key });
foreach (var post in page.Posts)
{
    if (Weight.TryRead(post.Data, out var reading)) { /* reading is a WeightReading */ }
}
```

Values are written and read with System.Text.Json (camelCase). Pass your own `JsonSerializerOptions` as the second
argument to change that. To generate the record from the registered schema (`GET /data-types/weight-reading/schema`),
use NJsonSchema or `quicktype --lang cs --src-lang schema`.

## Errors

```csharp
try
{
    await member.Posts.CreateAsync(new PostInput { Content = content });
}
catch (FeedFabricException e) when (e.Code == "content.violation")
{
    ShowRules(e.Params["category"].GetString());
}
```

## Tests

`dotnet test --project tests/FeedFabric.Sdk.Tests`
