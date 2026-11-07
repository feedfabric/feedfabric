using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using StackExchange.Redis;
using FeedFabric.Simulator;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<ILikeService, LikeService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IHomeFeedService, HomeFeedService>();
// Home feeds sign image URLs, so the simulator needs the same storage settings as the API
builder.Services.AddBlobStorage(builder.Configuration);
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IImpressionService, ImpressionService>();
builder.Services.AddScoped<FeedLog>();
builder.Services.AddScoped<FeatureBuilder>();
builder.Services.AddScoped<ForYouService>();
builder.Services.Configure<RankingOptions>(builder.Configuration.GetSection(RankingOptions.Section));
builder.Services.AddScoped<RankingProfileStore>();
// The trained ranker, loaded from blob storage and checked for new versions once a minute
builder.Services.AddSingleton<RankerModel>();
builder.Services.Configure<ModerationOptions>(builder.Configuration.GetSection(ModerationOptions.Section));
builder.Services.AddSingleton<ToxicityClassifier>();
builder.Services.AddSingleton<TextModerator>();
builder.Services.AddScoped<EmbeddingStore>();
// The last day's post embeddings in memory, for meaning-based candidates
builder.Services.AddSingleton<EmbeddingIndex>();
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.Section));
// abortConnect=false: home feeds fall back to Postgres while Redis is down
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisOptions = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisOptions.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(redisOptions);
});
builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection(SimulatorOptions.Section));
builder.Services.AddSingleton<Backfill>();
builder.Services.AddHostedService<Simulation>();

var host = builder.Build();

// Local dev: bring the database up to date on startup, so this runs on a fresh database whether or not the API has
// started yet. Other environments migrate as a deploy step.
if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
{
    using var scope = host.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
await host.RunAsync();
