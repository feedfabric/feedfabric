using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace FeedFabric.Api.Auth;

// Local dev only: reads the Bearer token's claims without checking its signature or expiry.
// Without a token, "X-Dev-User: 2" signs in as user 2; with neither, the user is DefaultUserId.
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";
    public const string DefaultUserId = "1";
    public const string UserHeader = "X-Dev-User";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    // Unsigned JWT, so the same parsing path runs however the user is chosen
    public static string CreateToken(string userId) =>
        TokenHandler.CreateToken(new JsonObject { ["sub"] = userId, ["name"] = $"Dev User {userId}" }.ToJsonString());

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var devUser = Request.Headers[UserHeader].ToString().Trim();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : CreateToken(devUser.Length > 0 ? devUser : DefaultUserId);

        JsonWebToken jwt;
        try
        {
            jwt = TokenHandler.ReadJsonWebToken(token);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Malformed JWT: {ex.Message}"));
        }

        if (!jwt.TryGetPayloadValue<string>(JwtRegisteredClaimNames.Sub, out var sub) || string.IsNullOrEmpty(sub))
        {
            return Task.FromResult(AuthenticateResult.Fail("JWT has no sub claim."));
        }

        var identity = new ClaimsIdentity(jwt.Claims, SchemeName, JwtRegisteredClaimNames.Sub, "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
