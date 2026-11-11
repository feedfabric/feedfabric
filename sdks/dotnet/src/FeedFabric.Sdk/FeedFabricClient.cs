using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedFabric.Sdk;

internal static class FeedFabricJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record FeedFabricOptions
{
    /// <summary>Where the API runs, e.g. "https://feed.example.com".</summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>A key from the admin panel (Integrations → API keys): "ffk_…". Keep it on your servers.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Retries after a 429 or 503 (waiting Retry-After), and after network errors on requests that are safe to repeat.</summary>
    public int MaxRetries { get; init; } = 2;
}

/// <summary>
/// The SDK's entry point: one per process, shared by every request and safe for concurrent use. Pass an HttpClient
/// from IHttpClientFactory to control its handler and timeout; one is made otherwise.
/// </summary>
public sealed class FeedFabricClient
{
    public const string Version = "0.1.0";

    private readonly HttpClient http;
    private readonly Uri baseUrl;
    private readonly string apiKey;
    private readonly int maxRetries;

    // How often UploadAsync checks whether the image is ready; shortened in tests
    internal TimeSpan UploadPoll { get; set; } = TimeSpan.FromSeconds(1);

    public FeedFabricClient(FeedFabricOptions options, HttpClient? http = null)
    {
        if (!options.ApiKey.StartsWith("ffk_", StringComparison.Ordinal))
        {
            throw new ArgumentException("FeedFabric needs an API key from the admin panel (\"ffk_…\")", nameof(options));
        }
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        baseUrl = new Uri(options.BaseUrl.ToString().TrimEnd('/') + "/");
        apiKey = options.ApiKey;
        maxRetries = options.MaxRetries;
    }

    public FeedFabricClient(string baseUrl, string apiKey, HttpClient? http = null)
        : this(new FeedFabricOptions { BaseUrl = new Uri(baseUrl), ApiKey = apiKey }, http)
    {
    }

    /// <summary>
    /// Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId).
    /// </summary>
    public FeedFabricMember AsMember(string externalUserId, MemberProfile? profile = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(externalUserId);
        var headers = new List<KeyValuePair<string, string>> { new("FeedFabric-User", Uri.EscapeDataString(externalUserId)) };
        foreach (var (name, value) in new[]
                 {
                     ("FeedFabric-Username", profile?.Username), ("FeedFabric-Display-Name", profile?.DisplayName),
                     ("FeedFabric-Avatar", profile?.AvatarUrl),
                 })
        {
            if (!string.IsNullOrEmpty(value))
            {
                headers.Add(new(name, Uri.EscapeDataString(value)));
            }
        }
        return new FeedFabricMember(this, headers);
    }

    internal async Task<T?> SendAsync<T>(HttpMethod method, string path, IReadOnlyList<KeyValuePair<string, string>> headers,
        object? body, CancellationToken cancellationToken)
    {
        var url = new Uri(baseUrl, path.TrimStart('/'));
        var payload = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, FeedFabricJson.Options);
        // Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
        var repeatable = method != HttpMethod.Post;

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd($"feedfabric-dotnet/{Version}");
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
            if (payload is not null)
            {
                request.Content = new ByteArrayContent(payload) { Headers = { ContentType = new("application/json") } };
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException e) when (!cancellationToken.IsCancellationRequested)
            {
                if (repeatable && attempt < maxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    continue;
                }
                throw new FeedFabricException(0, "network_error", e.Message, inner: e);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = FeedFabricException.From(response, text);
                    var retry = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                (response.StatusCode == HttpStatusCode.ServiceUnavailable && repeatable);
                    if (retry && attempt < maxRetries)
                    {
                        await Task.Delay(error.RetryAfter ?? Backoff(attempt), cancellationToken);
                        continue;
                    }
                    throw error;
                }
                return text.Length == 0 ? default : JsonSerializer.Deserialize<T>(text, FeedFabricJson.Options);
            }
        }
    }

    // To the storage's own signed URL, which doesn't take the API key
    internal async Task PutFileAsync(string url, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new(contentType) } },
        };
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw FeedFabricException.From(response, await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, attempt), 4000) + Random.Shared.Next(100));
}

/// <summary>Everything a member can do, as that member.</summary>
public sealed class FeedFabricMember
{
    private readonly FeedFabricClient client;
    private readonly IReadOnlyList<KeyValuePair<string, string>> headers;

    internal FeedFabricMember(FeedFabricClient client, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        this.client = client;
        this.headers = headers;
        Posts = new Posts(this);
        Feeds = new Feeds(this);
        Comments = new Comments(this);
        Media = new MediaUploads(this, client);
        Signals = new Signals(this);
    }

    public Posts Posts { get; }
    public Feeds Feeds { get; }
    public Comments Comments { get; }
    public MediaUploads Media { get; }
    public Signals Signals { get; }

    internal async Task<T> CallAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        await client.SendAsync<T>(method, path, headers, body, cancellationToken) ??
        throw new FeedFabricException(0, "empty_response", $"{method} {path} returned nothing");

    internal Task CallAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        client.SendAsync<JsonElement>(method, path, headers, body, cancellationToken);

    /// <summary>Who FeedFabric thinks this is; also creates the member if they're new.</summary>
    public Task<Me> MeAsync(CancellationToken cancellationToken = default) =>
        CallAsync<Me>(HttpMethod.Get, "/me", null, cancellationToken);

    public Task FollowAsync(string externalUserId, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Put, $"/follows/{Esc(externalUserId)}", null, cancellationToken);

    public Task UnfollowAsync(string externalUserId, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Delete, $"/follows/{Esc(externalUserId)}", null, cancellationToken);

    public Task<IReadOnlyList<Favourite>> FavouritesAsync(CancellationToken cancellationToken = default) =>
        CallAsync<IReadOnlyList<Favourite>>(HttpMethod.Get, "/favourites", null, cancellationToken);

    /// <summary>Up to 30; favourite.limit_reached after that.</summary>
    public Task AddFavouriteAsync(string externalUserId, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Put, $"/favourites/{Esc(externalUserId)}", null, cancellationToken);

    public Task RemoveFavouriteAsync(string externalUserId, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Delete, $"/favourites/{Esc(externalUserId)}", null, cancellationToken);

    /// <summary>The registered data types and their schemas.</summary>
    public Task<IReadOnlyList<DataTypeInfo>> DataTypesAsync(CancellationToken cancellationToken = default) =>
        CallAsync<IReadOnlyList<DataTypeInfo>>(HttpMethod.Get, "/data-types", null, cancellationToken);

    public Task<DataTypeInfo> DataTypeAsync(string key, CancellationToken cancellationToken = default) =>
        CallAsync<DataTypeInfo>(HttpMethod.Get, $"/data-types/{Esc(key)}", null, cancellationToken);

    /// <summary>Everything held about the member, as one JSON document. A few an hour.</summary>
    public Task<JsonElement> ExportAccountAsync(CancellationToken cancellationToken = default) =>
        CallAsync<JsonElement>(HttpMethod.Get, "/me/export", null, cancellationToken);

    /// <summary>Erases the member: posts, comments and profile at once, the rest shortly after.</summary>
    public Task DeleteAccountAsync(CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Delete, "/me", null, cancellationToken);

    internal static string Esc(string value) => Uri.EscapeDataString(value);

    internal static string Query(params (string Name, object? Value)[] parameters)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            var text = value switch
            {
                null => null,
                PostOrder order => order.ToString().ToLowerInvariant(),
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (!string.IsNullOrEmpty(text))
            {
                query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Esc(text));
            }
        }
        return query.ToString();
    }
}

public sealed class Posts
{
    private readonly FeedFabricMember m;

    internal Posts(FeedFabricMember member) => m = member;

    public Task<SavedPost> CreateAsync(PostInput post, CancellationToken cancellationToken = default) =>
        m.CallAsync<SavedPost>(HttpMethod.Post, "/userfeed/posts", post, cancellationToken);

    /// <summary>Within an hour of posting. Null Tags or Data keep the post's; MediaIds are ignored.</summary>
    public Task<SavedPost> EditAsync(Guid postId, PostInput post, CancellationToken cancellationToken = default) =>
        m.CallAsync<SavedPost>(HttpMethod.Put, $"/userfeed/posts/{postId}", post with { MediaIds = null }, cancellationToken);

    public Task DeleteAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Delete, $"/userfeed/posts/{postId}", null, cancellationToken);

    /// <summary>The member's own posts.</summary>
    public Task<OwnPostsPage> ListAsync(OwnPostsQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new OwnPostsQuery();
        var q = FeedFabricMember.Query(("type", query.Type), ("order", query.Order), ("limit", query.Limit), ("cursor", query.Cursor),
            ("gt", query.Gt), ("gte", query.Gte), ("lt", query.Lt), ("lte", query.Lte));
        return m.CallAsync<OwnPostsPage>(HttpMethod.Get, "/userfeed" + q, null, cancellationToken);
    }

    public Task LikeAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Put, $"/userfeed/posts/{postId}/like", null, cancellationToken);

    public Task UnlikeAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Delete, $"/userfeed/posts/{postId}/like", null, cancellationToken);

    public Task RepostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Put, $"/userfeed/posts/{postId}/repost", null, cancellationToken);

    public Task UnrepostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Delete, $"/userfeed/posts/{postId}/repost", null, cancellationToken);

    public Task ReportAsync(Guid postId, ReportReason reason, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Post, $"/userfeed/posts/{postId}/report", new { reason }, cancellationToken);
}

public sealed class Feeds
{
    private readonly FeedFabricMember m;

    internal Feeds(FeedFabricMember member) => m = member;

    /// <summary>Posts from the accounts the member follows, newest first.</summary>
    public Task<FeedPage> HomeAsync(int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<FeedPage>(HttpMethod.Get, "/userfeed/home" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null,
            cancellationToken);

    /// <summary>Ranked for the member; profile picks a ranking profile.</summary>
    public Task<ForYouPage> ForYouAsync(string? profile = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        m.CallAsync<ForYouPage>(HttpMethod.Get,
            "/userfeed/foryou" + FeedFabricMember.Query(("profile", profile), ("limit", limit), ("cursor", cursor)), null, cancellationToken);

    public Task<FeedPage> FavouritesAsync(int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<FeedPage>(HttpMethod.Get, "/userfeed/favourites" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null,
            cancellationToken);

    /// <summary>Posts with a tag (hashtag, inferred topic or app tag), newest first.</summary>
    public Task<FeedPage> TagAsync(string tag, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<FeedPage>(HttpMethod.Get,
            $"/userfeed/tags/{FeedFabricMember.Esc(tag)}/posts" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null,
            cancellationToken);

    public Task<IReadOnlyList<TrendingTag>> TrendingTagsAsync(int? hours = null, int? limit = null,
        CancellationToken cancellationToken = default) =>
        m.CallAsync<IReadOnlyList<TrendingTag>>(HttpMethod.Get,
            "/userfeed/tags/trending" + FeedFabricMember.Query(("hours", hours), ("limit", limit)), null, cancellationToken);

    public Task<MentionPage> MentionsAsync(int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<MentionPage>(HttpMethod.Get, "/userfeed/mentions" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null,
            cancellationToken);
}

public sealed class Comments
{
    private readonly FeedFabricMember m;

    internal Comments(FeedFabricMember member) => m = member;

    /// <summary>Top-level comments, oldest first.</summary>
    public Task<CommentPage> ListAsync(Guid postId, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<CommentPage>(HttpMethod.Get,
            $"/userfeed/posts/{postId}/comments" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null, cancellationToken);

    public Task<CommentPage> RepliesAsync(Guid commentId, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        m.CallAsync<CommentPage>(HttpMethod.Get,
            $"/userfeed/comments/{commentId}/replies" + FeedFabricMember.Query(("limit", limit), ("cursor", cursor)), null, cancellationToken);

    public Task<Comment> CreateAsync(Guid postId, string content, Guid? parentCommentId = null, CancellationToken cancellationToken = default) =>
        m.CallAsync<Comment>(HttpMethod.Post, $"/userfeed/posts/{postId}/comments", new { content, parentCommentId }, cancellationToken);

    public Task DeleteAsync(Guid commentId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Delete, $"/userfeed/comments/{commentId}", null, cancellationToken);

    public Task ReportAsync(Guid commentId, ReportReason reason, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Post, $"/userfeed/comments/{commentId}/report", new { reason }, cancellationToken);
}

public sealed class MediaUploads
{
    private readonly FeedFabricMember m;
    private readonly FeedFabricClient client;

    internal MediaUploads(FeedFabricMember member, FeedFabricClient client)
    {
        m = member;
        this.client = client;
    }

    /// <summary>Asks for an upload URL: then PUT the file there and call CompleteAsync.</summary>
    public Task<Upload> CreateUploadAsync(string contentType, long size, CancellationToken cancellationToken = default) =>
        m.CallAsync<Upload>(HttpMethod.Post, "/media/uploads", new { contentType, size }, cancellationToken);

    public Task<Media> CompleteAsync(Guid mediaId, CancellationToken cancellationToken = default) =>
        m.CallAsync<Media>(HttpMethod.Post, $"/media/{mediaId}/complete", null, cancellationToken);

    public Task<Media> GetAsync(Guid mediaId, CancellationToken cancellationToken = default) =>
        m.CallAsync<Media>(HttpMethod.Get, $"/media/{mediaId}", null, cancellationToken);

    /// <summary>
    /// The whole upload: asks for a URL, sends the file, and waits for scanning (a minute at most). Returns the ready
    /// image, for a post's MediaIds; throws a FeedFabricException "media.rejected" if it's refused, or "media.timeout"
    /// if it's still being checked.
    /// </summary>
    public async Task<Media> UploadAsync(byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var upload = await CreateUploadAsync(contentType, content.LongLength, cancellationToken);
        await client.PutFileAsync(upload.UploadUrl, content, upload.ContentType, cancellationToken);
        var media = await CompleteAsync(upload.MediaId, cancellationToken);
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (media.Status is "processing" or "awaitingUpload")
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new FeedFabricException(0, "media.timeout", "The image is still being checked; try GetAsync later.",
                    new Dictionary<string, JsonElement> { ["mediaId"] = JsonSerializer.SerializeToElement(media.Id) });
            }
            await Task.Delay(client.UploadPoll, cancellationToken);
            media = await GetAsync(upload.MediaId, cancellationToken);
        }
        if (media.Status == "rejected")
        {
            throw new FeedFabricException(422, "media.rejected", $"The image was rejected: {media.RejectionReason ?? "unknown reason"}",
                new Dictionary<string, JsonElement>
                {
                    ["mediaId"] = JsonSerializer.SerializeToElement(media.Id),
                    ["reason"] = JsonSerializer.SerializeToElement(media.RejectionReason),
                });
        }
        return media;
    }
}

/// <summary>What the member's reading tells the ranker. Send them from your server as your app reports them.</summary>
public sealed class Signals
{
    private readonly FeedFabricMember m;

    internal Signals(FeedFabricMember member) => m = member;

    /// <summary>Posts the member saw (at least half on screen for a second); up to 100 at a time.</summary>
    public Task<SignalsCounted> ImpressionsAsync(IEnumerable<Guid> postIds, CancellationToken cancellationToken = default) =>
        m.CallAsync<SignalsCounted>(HttpMethod.Post, "/userfeed/impressions", new { postIds }, cancellationToken);

    /// <summary>The member opened the post's own page.</summary>
    public Task<SignalsCounted> OpenAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync<SignalsCounted>(HttpMethod.Post, $"/userfeed/posts/{postId}/open", null, cancellationToken);

    /// <summary>How long posts stayed on screen; up to 100 at a time.</summary>
    public Task<SignalsCounted> DwellAsync(IEnumerable<DwellView> views, CancellationToken cancellationToken = default) =>
        m.CallAsync<SignalsCounted>(HttpMethod.Post, "/userfeed/dwell", new { views }, cancellationToken);

    public Task NotInterestedAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Post, $"/userfeed/posts/{postId}/not-interested", null, cancellationToken);

    public Task ShowMoreAsync(Guid postId, CancellationToken cancellationToken = default) =>
        m.CallAsync(HttpMethod.Post, $"/userfeed/posts/{postId}/show-more", null, cancellationToken);
}
