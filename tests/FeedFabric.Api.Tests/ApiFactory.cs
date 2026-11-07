using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace FeedFabric.Api.Tests;

// The API against real Postgres and Redis in containers, started once for every test in the "api" collection. It runs
// as Development, so startup migrates the database and Dev auth trusts the test's unsigned tokens. Kestrel listens on
// a free port. The models come from the repo's .blobs folder (make models); tests that need them skip without them.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string RepoBlobs = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.blobs"));

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer redis = new RedisBuilder("redis:7.4").Build();
    private readonly string blobs = Directory.CreateTempSubdirectory("feedfabric-api-tests-").FullName;

    // Extra settings for a test class that needs its own factory
    public Dictionary<string, string?> Settings { get; } = [];

    public string PostgresConnectionString => postgres.GetConnectionString();

    public string RedisConnectionString => redis.GetConnectionString();

    public static bool ModelsPresent => File.Exists(Path.Combine(RepoBlobs, "models/safety/toxicity/meta.json"));

    public async ValueTask InitializeAsync()
    {
        // Real Kestrel rather than the in-memory test server, so its limits (Kestrel:Limits) apply as in production
        UseKestrel(0);
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        if (Directory.Exists(Path.Combine(RepoBlobs, "models")))
        {
            Directory.CreateSymbolicLink(Path.Combine(blobs, "models"), Path.Combine(RepoBlobs, "models"));
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", redis.GetConnectionString());
        builder.UseSetting("BlobStorage:Provider", "Local");
        builder.UseSetting("BlobStorage:Local:Root", blobs);
        builder.UseSetting("BlobStorage:Local:SigningKey", "api-tests-blob-signing-key-0123456789");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
    }

    // A client signed in as the given user (Dev auth)
    public HttpClient ClientFor(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DevToken(userId, roles));
        return client;
    }

    // Unsigned, as Dev auth reads it; roles go in "roles", the default role claim
    private static string DevToken(string userId, string[] roles)
    {
        var payload = new JsonObject { ["sub"] = userId, ["name"] = $"Test User {userId}" };
        if (roles.Length > 0)
        {
            payload["roles"] = new JsonArray(roles.Select(role => JsonValue.Create(role)).ToArray<JsonNode?>());
        }
        return new JsonWebTokenHandler().CreateToken(payload.ToJsonString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), redis.DisposeAsync().AsTask());
        Directory.Delete(blobs, recursive: true);
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
