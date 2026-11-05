# Plugging in your sign-in

FeedFabric has no accounts, passwords or sign-in screens. It trusts the system it's plugged into: your identity
provider issues a token, and FeedFabric checks it and reads who the user is and what roles they have. Users are created
the first time they make a request.

All of it is configuration, under `Auth` (`src/FeedFabric.Api/Auth/AuthOptions.cs`). Nothing in the code changes from
one provider to the next.

## What FeedFabric needs from a token

| | Setting | Default | Notes |
|---|---|---|---|
| Who the user is | `Auth:Claims:UserId` | `sub` | Must never change for a person. Stored as `ExternalUserId`. |
| Username | `Auth:Claims:Username` | `preferred_username`, `nickname`, `username` | Public and @mention-able. Without one, the user is `user_<their ID>`. Email is never used unless you list it. |
| Display name | `Auth:Claims:DisplayName` | `name` | |
| Avatar | `Auth:Claims:Avatar` | `picture` | |
| Roles | `Auth:Claims:Roles` | `roles`, `role`, `permissions` | Every claim listed is read. A dotted path reads into a JSON claim, e.g. `realm_access.roles`. |
| Which roles are moderators | `Auth:Roles:Moderator` | `moderator` | Your role names. Moderators use the admin app and `/admin/moderation`. |
| Which roles manage ranking | `Auth:Roles:RankingAdmin` | `ranking-admin` | Saving ranking profiles changes everyone's feed. |

Lists you set replace the defaults; they're never added to them.

Usernames and display names refresh from the token at most once an hour (`Auth:ProfileRefresh`). Set
`Auth:ProvisionUsers` to `false` if you'd rather create users yourself.

## Checking tokens

`Auth:Mode` is `Jwt` everywhere except local development, where `appsettings.Development.json` sets `Dev` (any token,
or `X-Dev-User: <id>`, is trusted without checking). `Dev` refuses to start outside Development.

In `Jwt` mode a token must be signed by your provider, unexpired, and issued for `Auth:Jwt:Audience`. Anything else is
a 401. Pick one way to check signatures:

- **`Auth:Jwt:Authority`**: an OpenID Connect issuer. FeedFabric reads its discovery document
  (`/.well-known/openid-configuration`) and keeps its signing keys up to date, so key rotation needs nothing.
  This is the one to use.
- **`Auth:Jwt:SigningKey` and `Auth:Jwt:Issuer`**: for a system that signs its own tokens with a shared secret (HS256).
  Anyone with the secret can make tokens for any user, so keep it in a secret store and prefer an Authority if you
  have one.

## Auth0

1. In Auth0, create an API with the identifier `https://feedfabric.example.com` (any URI; it's the audience).
2. Have your apps request tokens for that audience.
3. For roles, turn on RBAC for the API and *Add Permissions in the Access Token*, or add roles with a post-login Action
   under a namespaced claim.

```json
"Auth": {
  "Mode": "Jwt",
  "Jwt": { "Authority": "https://your-tenant.eu.auth0.com/", "Audience": "https://feedfabric.example.com" },
  "Claims": { "Roles": ["permissions", "https://example.com/roles"] },
  "Roles": { "Moderator": ["moderate:content"], "RankingAdmin": ["manage:ranking"] }
}
```

Entra ID, Cognito, Okta, Keycloak, Firebase and Supabase work the same way: set the Authority and Audience, then
point `Claims` at wherever that provider puts roles (`roles` for Entra, `cognito:groups` for Cognito,
`realm_access.roles` for Keycloak).

## Your own sign-in server

A system that runs its own OpenID Connect server (IdentityServer, Duende, OpenIddict and the like) plugs in the same
way as a hosted provider. Point the Authority at it, and map whatever claims it already issues:

```json
"Auth": {
  "Mode": "Jwt",
  "Jwt": { "Authority": "https://auth.example.com", "Audience": "feedfabric" },
  "Claims": { "UserId": "account_id", "Roles": ["role", "service_role"] },
  "Roles": { "Moderator": ["support", "content:admin"], "RankingAdmin": ["admin"] }
}
```

- **Audience.** Register FeedFabric as its own API resource (audience `feedfabric`) and have your apps request it, so
  a token meant for another of your APIs isn't accepted here. If your tokens only carry one shared audience, use that.
- **Moderators.** If you have no moderator role yet, map the staff roles that already moderate elsewhere, or add one.
- **Names.** If your tokens carry no username, users appear as `user_<their ID>` until a token has one.

## The admin app

The admin app (`web/admin`) sends whatever token it's given as `Authorization: Bearer`. For now, paste a token from a
moderator's session into its header, or put the app behind a gateway that signs moderators in and adds the token, such
as oauth2-proxy or Cloudflare Access.
