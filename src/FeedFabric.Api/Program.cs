using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Admin;
using FeedFabric.Api.Dev;
using FeedFabric.Api.Storage;
using FeedFabric.Api.Errors;
using FeedFabric.Api.Hosting;
using FeedFabric.Api.RateLimiting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.DataTypes;
using FeedFabric.Application.Services;
using FeedFabric.Application.Services.Admin;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Health;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Favourites;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Reposts;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;
using FeedFabric.Hosting;
using StackExchange.Redis;

// Docker HEALTHCHECK: see HealthEndpoints.ProbeAsync
if (args is ["healthcheck", ..])
{
    return await HealthEndpoints.ProbeAsync();
}

var builder = WebApplication.CreateBuilder(args);
StartupChecks.Validate(builder.Configuration, builder.Environment.IsDevelopment(), "ConnectionStrings:Redis");
builder.AddFeedFabricTelemetry("feedfabric-api", [], []);

// How long in-flight requests get to finish on SIGTERM; keep the orchestrator's grace period longer than this
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = builder.Configuration.GetValue("ShutdownTimeout", TimeSpan.FromSeconds(25)));

// Kestrel:Limits, e.g. MaxRequestBodySize (1 MB in appsettings.json; uploads go to signed URLs, not through here)
builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));
// No "Server: Kestrel" on responses
builder.Services.PostConfigure<KestrelServerOptions>(options => options.AddServerHeader = false);

// Add services to the container.
// Pooled: contexts are reset and reused across requests instead of being built for each one
builder.Services.AddDbContextPool<AppDbContext>(options =>
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
// The kinds of structured data the host's apps attach to posts, each with its JSON Schema
builder.Services.AddScoped<DataTypeStore>();
builder.Services.AddScoped<IFollowService, FollowService>();
builder.Services.AddScoped<IFavouriteService, FavouriteService>();
builder.Services.AddScoped<ILikeService, LikeService>();
builder.Services.AddScoped<IRepostService, RepostService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IMentionService, MentionService>();
builder.Services.AddBlobStorage(builder.Configuration);
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.Section));
// Only read for the admin panel; the worker does the scanning
builder.Services.Configure<MalwareScanningOptions>(builder.Configuration.GetSection(MalwareScanningOptions.Section));
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
// Topics inferred from wording (Topics:Keywords, or the built-in wellness ones)
builder.Services.AddTopics(builder.Configuration);
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AdminPostService>();
builder.Services.AddScoped<EmbeddingStore>();
// The last day's post embeddings in memory, for meaning-based candidates
builder.Services.AddSingleton<EmbeddingIndex>();

// Who's calling: FeedFabric trusts the host's identity provider rather than having accounts (AuthOptions)
builder.Services.AddFeedFabricAuth(builder.Configuration, builder.Environment);
builder.Services.AddAuthorizationBuilder().AddRankingPolicies(builder.Environment).AddModerationPolicies(builder.Environment)
    .AddAdminPolicies(builder.Environment);
builder.Services.AddHttpContextAccessor();
// Behind a TLS-terminating proxy (ProxyOptions)
var proxy = builder.Configuration.GetSection(ProxyOptions.Section).Get<ProxyOptions>() ?? new ProxyOptions();
builder.Services.Configure<ForwardedHeadersOptions>(proxy.ApplyTo);
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
// Browsers on these origins (the admin panel, a web front end) may call the API. Tokens go in the Authorization
// header, not cookies, so credentials aren't allowed. Empty: no CORS headers, serve front ends from the API's origin.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Retry-After")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));
// /health/live and /health/ready. Without Redis the API still serves (feeds fall back to Postgres), so it only
// degrades readiness; without the toxicity model nothing can be posted.
builder.Services.AddHealthChecks()
    .AddPostgres()
    .AddRedis(HealthStatus.Degraded)
    .AddModels(HealthStatus.Unhealthy, ToxicityClassifier.Prefix);
// Per-user limits on writes, signals and previews (RateLimitOptions)
builder.Services.AddFeedFabricRateLimiting(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
// Every error, including auth, routing and unhandled exceptions, is Problem Details with a "code"
builder.Services.AddExceptionHandler<DependencyDownHandler>();
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
    await TagBackfill.RunAsync(db, scope.ServiceProvider.GetRequiredService<TopicRules>(), app.Logger);
}

if (!app.Environment.IsDevelopment() && app.Configuration["AllowedHosts"] is null or "*")
{
    app.Logger.LogWarning("AllowedHosts is \"*\": set it to the API's host names so requests for other hosts are refused");
}

// Configure the HTTP request pipeline.
if (proxy.Enabled)
{
    app.UseForwardedHeaders();
}
app.UseMiddleware<SecurityHeaders>();
// Kestrel's own refusals (a body over the limit is 413) keep their status rather than becoming a 500
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is Microsoft.AspNetCore.Http.BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The admin panel's overview; the panel itself (web/admin) is a separate static site
app.MapDashboardEndpoints();

// Not the health checks: probes call the container over plain HTTP
app.UseWhen(http => !http.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();
// After authentication, so limits are counted per user
app.UseRateLimiter();
// A signed-in user's first request creates their FeedFabric user from the token
app.UseMiddleware<UserProvisioning>();
app.UseMiddleware<SuspendedUsers>();
app.UseMiddleware<StaffAudit>();

app.MapControllers();
app.MapFeedFabricHealth();

if (app.Services.GetService<LocalBlobStorage>() is not null)
{
    app.MapLocalBlobEndpoints();
}

app.MapGet("/me", (ICurrentUser currentUser) => new { currentUser.ExternalUserId })
    .RequireAuthorization();

await app.RunAsync();
return 0;
