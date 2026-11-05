using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FeedFabric.Api.Auth;

// Reads the user out of whatever token the host's identity provider issued, as AuthOptions.Claims says, and adds
// it in FeedFabric's own claim names. Everything else in the app reads only these, so nothing else changes from
// one identity provider to the next.
public sealed class IdentityClaims(IOptions<AuthOptions> options) : IClaimsTransformation
{
    public const string AuthenticationType = "FeedFabric";
    public const string UserId = "ff:user";
    public const string Username = "ff:username";
    public const string DisplayName = "ff:name";
    public const string Avatar = "ff:avatar";
    public const string Role = "ff:role";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Runs on every authentication; only add ours once
        if (principal.Identities.Any(i => i.AuthenticationType == AuthenticationType) || principal.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal);
        }

        var claims = options.Value.Claims;
        var userId = Values(principal, claims.UserId).FirstOrDefault();
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(principal);
        }

        var identity = new ClaimsIdentity(AuthenticationType, UserId, Role);
        identity.AddClaim(new Claim(UserId, userId));
        Add(identity, Username, claims.UsernameOrDefault.SelectMany(c => Values(principal, c)).FirstOrDefault());
        Add(identity, DisplayName, claims.DisplayNameOrDefault.SelectMany(c => Values(principal, c)).FirstOrDefault());
        Add(identity, Avatar, Values(principal, claims.Avatar).FirstOrDefault());

        var theirs = claims.RolesOrDefault.SelectMany(c => Values(principal, c))
            .SelectMany(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = options.Value.Roles;
        if (roles.ModeratorOrDefault.Any(theirs.Contains))
        {
            identity.AddClaim(new Claim(Role, ModerationPolicies.ReviewerRole));
        }
        if (roles.RankingAdminOrDefault.Any(theirs.Contains))
        {
            identity.AddClaim(new Claim(Role, RankingPolicies.AdminRole));
        }

        principal.AddIdentity(identity);
        return Task.FromResult(principal);
    }

    private static void Add(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(type, value.Trim()));
        }
    }

    // A claim's values: the claim itself if one has the whole name, otherwise the path read into a JSON claim
    private static IEnumerable<string> Values(ClaimsPrincipal principal, string name)
    {
        var whole = principal.FindAll(name).ToList();
        if (whole.Count > 0)
        {
            return whole.SelectMany(c => Flatten(c.Value));
        }

        var dot = name.IndexOf('.');
        if (dot < 0 || principal.FindFirst(name[..dot]) is not { } root)
        {
            return [];
        }
        try
        {
            using var json = JsonDocument.Parse(root.Value);
            var element = json.RootElement;
            foreach (var part in name[(dot + 1)..].Split('.'))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
                {
                    return [];
                }
            }
            return Strings(element).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // A claim value that is itself a JSON array (some handlers keep arrays whole) becomes its items
    private static IEnumerable<string> Flatten(string value)
    {
        if (value.StartsWith('['))
        {
            try
            {
                using var json = JsonDocument.Parse(value);
                return Strings(json.RootElement).ToList();
            }
            catch (JsonException)
            {
                // Not JSON after all; use it as it is
            }
        }
        return [value];
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [element.GetRawText()],
        _ => [],
    };
}
