using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Dev;
using FeedFabric.Api.Storage;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));

    if (builder.Environment.IsDevelopment())
    {
        options.UseSeeding(DevUserSeeder.Seed).UseAsyncSeeding(DevUserSeeder.SeedAsync);
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IFeedService, FeedService>();
builder.Services.AddScoped<IHomeFeedService, HomeFeedService>();
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.Section));
// abortConnect=false: start even if Redis is down; the home feed falls back to Postgres until it's back
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisOptions = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    redisOptions.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(redisOptions);
});
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<IFollowService, FollowService>();
builder.Services.AddScoped<ILikeService, LikeService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IMentionService, MentionService>();
builder.Services.AddBlobStorage(builder.Configuration);
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.Section));
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IImpressionService, ImpressionService>();
builder.Services.AddScoped<FeedLog>();
builder.Services.AddScoped<FeatureBuilder>();
builder.Services.AddScoped<ForYouService>();
// Ranking profiles: presets plus saved ones; Ranking:DefaultProfile picks the one For You uses by default
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

// Local dev: every request is user 1, or the user named in an X-Dev-User header or Bearer token.
// Swap for AddJwtBearer with the real issuer before running outside Development.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);
}
builder.Services.AddAuthorizationBuilder().AddRankingPolicies(builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
// Every error, including auth, routing and unhandled exceptions, is Problem Details with a "code"
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions.TryAdd("code", ProblemResults.CodeForStatus(context.ProblemDetails.Status)));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers(options =>
        options.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(() => ErrorCodes.Required))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ValidationProblems.Create);

var app = builder.Build();

// Local emulators like LocalStack start empty; real buckets are provisioned outside the app
if (app.Services.GetRequiredService<IOptions<BlobStorageOptions>>().Value is { Provider: var provider, S3.CreateBucket: true }
    && provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<S3BlobStorage>().EnsureBucketAsync();
}

// Local dev: bring the database up to date on startup, which also runs the dev user seeder.
// Other environments migrate as a deploy step.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await TagBackfill.RunAsync(db, app.Logger);
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevEndpoints();
    app.MapDashboardEndpoints();
    // The simulator app, built from web/simulator into wwwroot. Its client-side routes all load index.html.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    foreach (var route in new[] { "live", "home", "mentions", "dashboard", "studio/{**path}", "studio", "u/{**path}", "post/{**path}", "tags/{**path}", "by/{**path}" })
    {
        app.MapFallbackToFile(route, "index.html");
    }
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Services.GetService<LocalBlobStorage>() is not null)
{
    app.MapLocalBlobEndpoints();
}

app.MapGet("/me", (ICurrentUser currentUser) => new { currentUser.ExternalUserId })
    .RequireAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
