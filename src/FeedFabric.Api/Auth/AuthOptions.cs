namespace FeedFabric.Api.Auth;

// How FeedFabric trusts the system it's plugged into. It has no accounts or sign-in of its own: the host's identity
// provider (Auth0, Entra ID, Cognito, Keycloak, Firebase, or the host's own API) issues tokens, and this says how to
// check them and where in them to find the user and their roles.
public class AuthOptions
{
    public const string Section = "Auth";

    // "Jwt" checks bearer tokens from the host's identity provider. "Dev" (Development only) trusts any token, or
    // X-Dev-User, without checking signatures.
    public string Mode { get; set; } = AuthModes.Jwt;

    public JwtAuthOptions Jwt { get; set; } = new();

    public ClaimNames Claims { get; set; } = new();

    // The host's role names that count as each of FeedFabric's roles
    public RoleNames Roles { get; set; } = new();

    // Create a user on their first request, from the token's claims, and refresh their profile from later tokens.
    // Turn off if the host would rather create users itself.
    public bool ProvisionUsers { get; set; } = true;

    // How long a user's profile from their token is trusted before the next request refreshes it
    public TimeSpan ProfileRefresh { get; set; } = TimeSpan.FromHours(1);
}

public static class AuthModes
{
    public const string Jwt = "Jwt";
    public const string Dev = "Dev";
}

public class JwtAuthOptions
{
    // An OpenID Connect issuer, e.g. "https://your-tenant.eu.auth0.com/". Its signing keys are fetched from its
    // discovery document and kept up to date, so key rotation needs nothing here.
    public string? Authority { get; set; }

    // The API identifier tokens must be issued for (Auth0's API audience). Required.
    public string? Audience { get; set; }

    // For a host that signs its own tokens with a shared secret (HS256) rather than publishing keys: the secret, and
    // the "iss" its tokens carry. Keep the secret in a secret store, not in appsettings.
    public string? SigningKey { get; set; }

    public string? Issuer { get; set; }

    // Allowed difference between the host's clock and ours
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(2);

    // The Authority's keys are only fetched over HTTPS. Turn off only for an identity provider on localhost.
    public bool RequireHttpsMetadata { get; set; } = true;
}

// Which claim holds each thing. A dotted path reads into a JSON claim ("realm_access.roles" for Keycloak); a claim
// whose own name has dots, like Auth0's namespaced "https://example.com/roles", is matched whole first.
// The lists start empty and take their defaults only when configuration leaves them empty: the configuration binder
// adds to a list rather than replacing it, which would quietly keep a default role alongside the host's own.
public class ClaimNames
{
    // The host's ID for the user; stored as UserEntity.ExternalUserId, so it must never change for a person
    public string UserId { get; set; } = "sub";

    // First one present wins
    public List<string> Username { get; set; } = [];

    public List<string> DisplayName { get; set; } = [];

    public string Avatar { get; set; } = "picture";

    // Every claim listed is read; values can be one, many, or space-separated (like "scope")
    public List<string> Roles { get; set; } = [];

    // Never email by default: usernames are public, and an address's first half often names the person. With none of
    // these, a user is "user_<their ID>" until a later token has a name.
    public IReadOnlyList<string> UsernameOrDefault => Username.Count > 0 ? Username : ["preferred_username", "nickname", "username"];

    public IReadOnlyList<string> DisplayNameOrDefault => DisplayName.Count > 0 ? DisplayName : ["name"];

    public IReadOnlyList<string> RolesOrDefault => Roles.Count > 0 ? Roles : ["roles", "role", "permissions"];
}

public class RoleNames
{
    public List<string> Moderator { get; set; } = [];

    public List<string> RankingAdmin { get; set; } = [];

    public IReadOnlyList<string> ModeratorOrDefault => Moderator.Count > 0 ? Moderator : [ModerationPolicies.ReviewerRole];

    public IReadOnlyList<string> RankingAdminOrDefault => RankingAdmin.Count > 0 ? RankingAdmin : [RankingPolicies.AdminRole];
}
