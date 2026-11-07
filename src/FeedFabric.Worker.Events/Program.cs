using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Worker.Engagement;
using FeedFabric.Worker.Events.FanOut;
using FeedFabric.Worker.Events.Embeddings;
using FeedFabric.Worker.Events.Kafka;
using FeedFabric.Worker.Events.Media;
using FeedFabric.Worker.Events.Outbox;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Pooled: each event gets its own scope, and so a context, which is reset and reused rather than built again
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);

var kafkaOptions = builder.Configuration.GetSection(KafkaOptions.Section).Get<KafkaOptions>() ?? new KafkaOptions();
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.Section));
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.Section));

// One producer for the whole process; it's thread-safe and batches internally
builder.Services.AddSingleton(_ => new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = kafkaOptions.BootstrapServers,
    Acks = Acks.All,
    EnableIdempotence = true,
}).Build());

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

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

await KafkaTopics.EnsureCreatedAsync(kafkaOptions);
await host.RunAsync();
