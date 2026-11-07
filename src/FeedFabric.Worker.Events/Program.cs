using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Health;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Worker.Engagement;
using FeedFabric.Worker.Events;
using FeedFabric.Worker.Events.FanOut;
using FeedFabric.Worker.Events.Embeddings;
using FeedFabric.Worker.Events.Kafka;
using FeedFabric.Worker.Events.Media;
using FeedFabric.Worker.Events.Outbox;
using FeedFabric.Hosting;
using StackExchange.Redis;

// Docker HEALTHCHECK: see HealthEndpoints.ProbeAsync
if (args is ["healthcheck", ..])
{
    return await HealthEndpoints.ProbeAsync();
}

// A web host only to serve /health/live and /health/ready (port 8080 in the container); the work is the hosted services
var builder = WebApplication.CreateSlimBuilder(args);
StartupChecks.Validate(builder.Configuration, builder.Environment.IsDevelopment(), "ConnectionStrings:Redis", "Kafka:BootstrapServers");
builder.AddFeedFabricTelemetry("feedfabric-worker", [WorkerMetrics.Name], [WorkerMetrics.Name]);

// Pooled: each event gets its own scope, and so a context, which is reset and reused rather than built again
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);

var kafkaOptions = builder.Configuration.GetSection(KafkaOptions.Section).Get<KafkaOptions>() ?? new KafkaOptions();
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.Section));
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.Section));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.Section));

// One producer for the whole process; it's thread-safe and batches internally
builder.Services.AddSingleton(_ =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = kafkaOptions.BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
    };
    kafkaOptions.Security.ApplyTo(config);
    return new ProducerBuilder<string, string>(config).Build();
});

// abortConnect=false: start even if Redis is down, and reconnect when it's back; handlers retry until then
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisOptions = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisOptions.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(redisOptions);
});

builder.Services.AddScoped<FeedFanOutHandler>();
builder.Services.AddScoped<LikeHandler>();
builder.Services.AddScoped<CommentHandler>();
builder.Services.AddBlobStorage(builder.Configuration);
builder.Services.AddMalwareScanning(builder.Configuration);
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.Section));
builder.Services.AddScoped<MediaProcessor>();
builder.Services.Configure<ModerationOptions>(builder.Configuration.GetSection(ModerationOptions.Section));
builder.Services.AddSingleton<NudityClassifier>();
builder.Services.AddSingleton<TextEmbedder>();
builder.Services.AddScoped<EmbeddingStore>();
builder.Services.AddScoped<PostEmbeddingHandler>();
// The worker can't do its job without any of these, so each makes it unready
builder.Services.AddSingleton<KafkaHealthCheck>();
builder.Services.AddHealthChecks()
    .AddPostgres()
    .AddRedis(HealthStatus.Unhealthy)
    .AddClamAv()
    .AddCheck<KafkaHealthCheck>("kafka", HealthStatus.Unhealthy, [HealthTags.Ready])
    .AddModels(HealthStatus.Unhealthy, NudityClassifier.Prefix, TextEmbedder.Prefix);

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<PostEventsConsumer>();
builder.Services.AddHostedService<EngagementEventsConsumer>();
builder.Services.AddHostedService<MediaEventsConsumer>();
builder.Services.AddHostedService<PostEmbeddingsConsumer>();

var host = builder.Build();

// Local dev: bring the database up to date on startup, so this runs on a fresh database whether or not the API has
// started yet. Other environments migrate as a deploy step.
if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
{
    using var scope = host.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (!host.Services.GetRequiredService<IOptions<MalwareScanningOptions>>().Value.Enabled)
{
    host.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning("Malware scanning is off (MalwareScanning:Enabled=false): uploads are published without a ClamAV scan");
}

host.MapFeedFabricHealth();

await KafkaTopics.EnsureCreatedAsync(kafkaOptions);
await host.RunAsync();
return 0;
