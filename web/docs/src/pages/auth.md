---
layout: ../layouts/Doc.astro
title: Sign-in and users
description: FeedFabric has no accounts. Your identity provider signs people in; FeedFabric checks its tokens, reads who they are and what roles they have, and creates them on their first request.
---

## The model

- Your app signs the member in with your identity provider (Auth0, Entra ID, Cognito, Okta, Keycloak, Firebase,
  Supabase, or your own OpenID Connect server) and gets an **access token for FeedFabric's audience**.
- Every call sends it: `Authorization: Bearer <token>`. Every route except the signed blob links needs one;
  a missing or bad token is `401 unauthorized`.
- FeedFabric checks the token, then reads the user's ID, name, avatar and roles from its claims (`IdentityClaims`).
- **On the first request** the `UserProvisioning` middleware creates the user from the token; later tokens refresh the
  username, display name and avatar at most once an hour (`Auth:ProfileRefresh`).

All of it is configuration under `Auth`; nothing in the code changes between providers. The repository's
`docs/auth.md` has worked examples for Auth0 and your own OIDC server.

## Checking tokens

| `Auth:Mode` | Where | What it trusts |
|---|---|---|
| `Jwt` (default) | Everywhere | Tokens signed by your provider, unexpired, for `Auth:Jwt:Audience` |
| `Dev` | Development only (refuses to start elsewhere) | Any token without checking it, or `X-Dev-User` |

For `Jwt`, set an audience and one way to check signatures:

```json
"Auth": {
  "Mode": "Jwt",
  "Jwt": { "Authority": "https://your-tenant.eu.auth0.com/", "Audience": "https://feedfabric.example.com" },
  "Claims": { "Roles": ["permissions", "https://example.com/roles"] },
  "Roles": { "Moderator": ["moderate:content"], "RankingAdmin": ["manage:ranking"] }
}
```

- **`Auth:Jwt:Authority`** (recommended): an OpenID Connect issuer. FeedFabric reads its discovery document and keeps
  its signing keys up to date, so key rotation needs nothing.
- **`Auth:Jwt:SigningKey` and `Auth:Jwt:Issuer`**: for a system that signs its own tokens with a shared HS256 secret
  of at least 32 bytes. Anyone with the secret can mint tokens for anyone, so keep it in a secret store.

The API refuses to start in `Jwt` mode without an audience, or with both or neither of Authority and SigningKey. A
valid token with no user ID claim is a `401`.

## Claims

| What | Setting | Default |
|---|---|---|
| User ID (stored as `ExternalUserId`, must never change for a person) | `Auth:Claims:UserId` | `sub` |
| Username (public, @mention-able) | `Auth:Claims:Username` | `preferred_username`, `nickname`, `username`; first present wins |
| Display name | `Auth:Claims:DisplayName` | `name` |
| Avatar URL | `Auth:Claims:Avatar` | `picture` |
| Roles | `Auth:Claims:Roles` | `roles`, `role`, `permissions`; every one listed is read |

A dotted path reads into a JSON claim (`realm_access.roles` for Keycloak). Lists you set **replace** the defaults.
Email is never used unless you list it.

Usernames become lowercase letters, digits and underscores, up to 30 characters; a taken name gets a number
(`maya_2`). With no username claim, the user is `user_<their ID>` until a token carries one. Set
`Auth:ProvisionUsers` to `false` to create users yourself; requests from users who don't exist yet then fail with
`404 user.not_synced`.

## Roles

| FeedFabric role | Setting (your role names) | Default | Can |
|---|---|---|---|
| Moderator | `Auth:Roles:Moderator` | `moderator` | `/admin/moderation`, `/admin/users`, `/admin/posts`, `/admin/media` |
| Ranking admin | `Auth:Roles:RankingAdmin` | `ranking-admin` | Everything under `/ranking`: profiles, versions, preview, vocabulary |
| Integration admin | `Auth:Roles:IntegrationAdmin` | `integration-admin` | [API keys](/sdks/) (`/admin/api-keys`) and [data types](/sdks/typed-data/) (`/admin/data-types`) |
| Staff (any of these) | | | `/admin/dashboard`, `/admin/system`, `/admin/feed/{externalUserId}/…` |

Members need no role. `GET /admin/me` works for anyone signed in and says what they can do:

```json
{ "externalUserId": "auth0|65f…", "username": "maya_runs", "displayName": "Maya", "moderator": false,
  "rankingAdmin": false, "integrationAdmin": false, "environment": "Production" }
```

## Development: X-Dev-User

`appsettings.Development.json` sets `Auth:Mode` to `Dev`. Then:

- a `Bearer` token is read **without checking its signature or expiry**, so you can hand-craft one with any `sub` and
  `roles`;
- with no token, `X-Dev-User: <externalUserId>` signs you in as that user;
- with neither, you're user `1`.

Users `1`, `2` and `3` (`dev1` to `dev3`) are seeded on startup, the simulator's are `sim-1` to `sim-1000`, and any
other ID is created on its first request. **In Development every signed-in user passes the staff, moderator,
ranking-admin and integration-admin policies**, and `GET /admin/me` says `true` for every role. API keys never do.

Your own servers sign in differently, with an API key and the member they act for: see
[API keys and SDKs](/sdks/).

```ts
// What the admin panel does (web/admin/src/api.ts): a token if it has one, X-Dev-User otherwise
const headers: Record<string, string> = session.token
  ? { Authorization: `Bearer ${session.token}` }
  : { 'X-Dev-User': session.devUser }
```

## Getting tokens to a browser app

Use your provider's SPA SDK (Auth0 SPA, MSAL, oidc-client-ts and the like) with the authorization code flow and
PKCE, requesting FeedFabric's audience, and give the API client a function that returns a fresh access token. See
[API client](/build-a-ui/api-client/#signing-in) for the shape. The admin panel today takes a pasted token, or sits
behind a gateway that signs staff in and adds the header (oauth2-proxy, Cloudflare Access).
