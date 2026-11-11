using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FeedFabric.Application.Services.DataTypes;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace FeedFabric.Worker.Tests;

// The whole event pipeline: the real worker host (its Program, consumers, outbox publisher) against Postgres, Redis
// and Kafka in containers, and on the other side the Application services the API's controllers call, wired the same
// way. A test does what a member would through the API, then waits for the worker to have done its part.
public sealed class PipelineFixture : IAsyncLifetime
{
    private static readonly string RepoBlobs = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.blobs"));

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer redis = new RedisBuilder("valkey/valkey:8").Build();
    private readonly KafkaContainer kafka = new KafkaBuilder("apache/kafka:4.0.0").Build();
    private readonly string blobs = Directory.CreateTempSubdirectory("feedfabric-pipeline-").FullName;
    private WorkerFactory? worker;

    public static bool ModelsPresent => File.Exists(Path.Combine(RepoBlobs, "models/safety/toxicity/meta.json"))
        && File.Exists(Path.Combine(RepoBlobs, "models/safety/nsfw/meta.json"));

    // The API's side
    public ServiceProvider Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync(), kafka.StartAsync());
        if (Directory.Exists(Path.Combine(RepoBlobs, "models")))
        {
            Directory.CreateSymbolicLink(Path.Combine(blobs, "models"), Path.Combine(RepoBlobs, "models"));
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = redis.GetConnectionString(),
            ["Kafka:BootstrapServers"] = kafka.GetBootstrapAddress(),
            ["Kafka:ReplicationFactor"] = "1",
            ["BlobStorage:Provider"] = "Local",
            ["BlobStorage:Local:Root"] = blobs,
            ["BlobStorage:Local:SigningKey"] = "pipeline-tests-blob-signing-key-0123456789",
            ["MalwareScanning:Enabled"] = "false",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .AddDbContext<AppDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()))
            .AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis.GetConnectionString()))
            .AddSingleton(TimeProvider.System)
            .Configure<FeedOptions>(configuration.GetSection(FeedOptions.Section))
            .Configure<MediaOptions>(configuration.GetSection(MediaOptions.Section))
            .Configure<ModerationOptions>(configuration.GetSection(ModerationOptions.Section))
            .AddSingleton<ToxicityClassifier>()
            .AddSingleton<TextModerator>()
            .AddTopics(configuration)
            .AddScoped<FeedLog>()
            .AddScoped<FeatureBuilder>()
            .AddScoped<EmbeddingStore>()
            .AddScoped<IImpressionService, ImpressionService>()
            .AddScoped<IMediaService, MediaService>()
            .AddScoped<IPostService, PostService>()
            .AddMemoryCache()
            .AddScoped<DataTypeStore>()
            .AddScoped<ILikeService, LikeService>()
            .AddScoped<ICommentService, CommentService>()
            .AddScoped<IHomeFeedService, HomeFeedService>()
            .AddScoped<ReportService>()
            .AddScoped<ReviewService>();
        services.AddBlobStorage(configuration);
        Api = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using (var scope = Api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        worker = new WorkerFactory(settings);
        // Starts the host, and with it the consumers and the outbox publisher
        _ = worker.Services;
    }

    // A member as the API would have provisioned them
    public async Task<UserEntity> UserAsync(string name)
    {
        using var scope = Api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new UserEntity { ExternalUserId = $"{name}-{Guid.NewGuid():N}", Username = $"{name}_{Guid.NewGuid():N}"[..24] };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // Polls until the condition holds, for the worker's asynchronous part
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {what}");
            }
            await Task.Delay(250);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (worker is not null)
        {
            await worker.DisposeAsync();
        }
        if (Api is not null)
        {
            await Api.DisposeAsync();
        }
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), redis.DisposeAsync().AsTask(), kafka.DisposeAsync().AsTask());
        Directory.Delete(blobs, recursive: true);
    }

    private sealed class WorkerFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}

[CollectionDefinition("pipeline")]
public sealed class PipelineCollection : ICollectionFixture<PipelineFixture>;
