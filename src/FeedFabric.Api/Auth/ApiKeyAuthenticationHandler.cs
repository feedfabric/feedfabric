using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.ApiKeys;

namespace FeedFabric.Api.Auth;

// The host's own servers, through an SDK: "Authorization: Bearer ffk_…", a key staff made in the admin panel, and
// "FeedFabric-User: <their member's ID>" to say who the call is for. The member is the same one their sign-in token
// would name (AuthOptions.Claims.UserId), and is created on first use like any other. Profile headers fill in what a
// token's claims would; their values are percent-encoded, so names can be any text.
// A key never carries a role: it can do what members can, and no staff route accepts it.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyStore store,
    IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string UserHeader = "FeedFabric-User";
    public const string UsernameHeader = "FeedFabric-Username";
    public const string DisplayNameHeader = "FeedFabric-Display-Name";
    public const string AvatarHeader = "FeedFabric-Avatar";
    public const int MaxUserIdLength = 200;

    // How long a key's check is trusted: a revoked key stops working at once on the server that revoked it, and
    // within this long on the others
    public static readonly TimeSpan CheckCacheTime = TimeSpan.FromMinutes(1);

    private const string MissingUser = "apikey:missing-user";

    // Whether a request carries an API key rather than a sign-in token
    public static bool HasKey(HttpRequest request) =>
        request.Headers.Authorization.ToString() is var header
        && header.StartsWith("Bearer " + ApiKeyStore.KeyPrefix, StringComparison.OrdinalIgnoreCase);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HasKey(Request))
        {
            return AuthenticateResult.NoResult();
        }
        var key = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var hash = ApiKeyStore.Hash(key);
        var keyId = await cache.GetOrCreateAsync(CacheKey(hash), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CheckCacheTime;
            return store.CheckAsync(hash, Context.RequestAborted);
        });
        if (keyId is null)
        {
            return AuthenticateResult.Fail("Unknown or revoked API key.");
        }

        var userId = Header(UserHeader);
        if (string.IsNullOrEmpty(userId) || userId.Length > MaxUserIdLength)
        {
            Context.Items[MissingUser] = true;
            return AuthenticateResult.Fail($"An API key acts for a member: send their ID in {UserHeader}.");
        }

        // Already in FeedFabric's own claim names, so IdentityClaims leaves it as it is
        var identity = new ClaimsIdentity(IdentityClaims.AuthenticationType, IdentityClaims.UserId, IdentityClaims.Role);
        identity.AddClaim(new Claim(IdentityClaims.UserId, userId));
        identity.AddClaim(new Claim(IdentityClaims.ApiKey, keyId.Value.ToString()));
        Add(identity, IdentityClaims.Username, Header(UsernameHeader));
        Add(identity, IdentityClaims.DisplayName, Header(DisplayNameHeader));
        Add(identity, IdentityClaims.Avatar, Header(AvatarHeader));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    // A good key without a member is the caller's mistake, not a bad credential: say exactly what's missing
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!Context.Items.ContainsKey(MissingUser))
        {
            await base.HandleChallengeAsync(properties);
            return;
        }
        var problem = ProblemResults.Create(Context, StatusCodes.Status401Unauthorized, ErrorCodes.ApiKeyUserRequired,
            $"An API key acts for a member: send their ID in the {UserHeader} header.", new { header = UserHeader });
        await problem.ExecuteResultAsync(new ActionContext { HttpContext = Context });
    }

    // Called when staff revoke a key, so it stops working at once on this server
    public static void Forget(IMemoryCache cache, string hash) => cache.Remove(CacheKey(hash));

    private string? Header(string name)
    {
        var value = Request.Headers[name].ToString().Trim();
        if (value.Length == 0)
        {
            return null;
        }
        try
        {
            return Uri.UnescapeDataString(value).Trim();
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static void Add(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }

    private static string CacheKey(string hash) => $"apikey:{hash}";
}
