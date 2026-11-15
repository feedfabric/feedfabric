---
layout: ../../layouts/Doc.astro
title: API keys and SDKs
description: Let your own servers call FeedFabric for your members, with an API key from the admin panel and a client library for TypeScript, Python, Go, C#, Java, PHP or Ruby.
---

Your members sign in to your app as they always have. Your app's server then calls FeedFabric for them: posting,
reading feeds, liking, commenting, uploading photos and sending reading signals. It needs no FeedFabric token per
member, only an **API key** and the ID of the member it's acting for.

## Making a key

1. Give someone the **integration admin** role. Map one of your role names to it with `Auth:Roles:IntegrationAdmin`
   (default `integration-admin`; see [Sign-in and users](/auth/#roles)).
2. In the admin panel, open **Integrations → API keys** and make a key for each server that will call FeedFabric.
   Name it after that server, such as `web-backend` or `ios-push-service`.
3. Copy the key (`ffk_…`) into that server's secret store. It's shown once: FeedFabric keeps only a hash.

The page shows each key's first characters, who made it and when, and when it was last used. **Revoke** stops a
key at once on the server that handled the revoke, and within a minute everywhere else. To rotate a key, make the
new one, deploy it, then revoke the old one.

## What a call looks like

```http
POST /userfeed/posts
Authorization: Bearer ffk_…
FeedFabric-User: auth0%7C65f2a1
FeedFabric-Username: maya_runs
FeedFabric-Display-Name: Maya
Content-Type: application/json

{ "content": "First run of the year #running" }
```

- **`FeedFabric-User`** (required) is the member's ID: the one their sign-in tokens carry in `Auth:Claims:UserId`.
  A member is the same person whether they reach FeedFabric through your server or with a token of their own.
  Without the header: `401 api_key.user_required`.
- **`FeedFabric-Username`, `FeedFabric-Display-Name`, `FeedFabric-Avatar`** (optional) are the profile FeedFabric
  uses the first time it sees the member. It refreshes them at most hourly, as it does from a token.
- Every header value is **percent-encoded** UTF-8, so `Zoë` is `Zo%C3%AB`.

**A key can only do what a member can.** It carries no roles, and every staff route refuses it, even in Development.
Anyone holding a key can act as any of your members, so keep it on your servers and never ship it in an app.

## The SDKs

The repository's [`sdks/`](https://github.com/feedfabric/feedfabric/tree/main/sdks) folder has a client for each
language your servers are likely to be in. They all have the same shape:

| Language | Package | Make a client |
|---|---|---|
| TypeScript / JavaScript | `@feedfabric/sdk` | `new FeedFabric({ baseUrl, apiKey })` |
| Python | `feedfabric` | `FeedFabric(base_url, api_key)` |
| Go | `github.com/feedfabric/feedfabric/sdks/go` | `feedfabric.New(baseURL, apiKey)` |
| C# / .NET | `FeedFabric.Sdk` | `new FeedFabricClient(baseUrl, apiKey)` |
| Java / Kotlin | `io.feedfabric:feedfabric-java` | `new FeedFabric(baseUrl, apiKey)` |
| PHP | `feedfabric/feedfabric-php` | `new FeedFabric($baseUrl, $apiKey)` |
| Ruby | `feedfabric` | `FeedFabric::Client.new(base_url:, api_key:)` |

From the client, `asMember(id, profile)` (`as_member`, `AsMember`) gives you everything that member can do:

| | |
|---|---|
| `posts` | `create`, `edit`, `delete`, `list` (their own, with `type` for [typed data](/sdks/typed-data/)), `like`, `unlike`, `repost`, `unrepost`, `report` |
| `feeds` | `home`, `forYou`, `favourites`, `tag`, `trendingTags`, `mentions` |
| `comments` | `list`, `replies`, `create`, `delete`, `report` |
| `media` | `upload` (the whole upload: URL, send, wait for scanning), or `createUpload`, `complete`, `get` |
| `signals` | `impressions`, `open`, `dwell`, `notInterested`, `showMore` |
| the member | `me`, `follow`, `unfollow`, `favourites`, `addFavourite`, `removeFavourite`, `dataTypes`, `exportAccount`, `deleteAccount` |

```python
feed = FeedFabric("https://feed.example.com", os.environ["FEEDFABRIC_API_KEY"])
member = feed.as_member(user.id, username=user.handle, display_name=user.name)

page = member.feeds.for_you(limit=20)
member.signals.impressions([post["id"] for post in page["posts"]])
```

### Errors and retries

Every failure is an error carrying the HTTP `status`, its stable [`code`](/api/errors/), `params`, `errors`,
`traceId` and `retryAfter`. Match on `code`. With no response at all, the code is `network_error`.

The SDKs retry up to twice by default. A `429` is always retried after `Retry-After`, because nothing happened on
the server. A `503`, or a network error, is retried only for `GET`, `PUT` and `DELETE`, which are safe to repeat. A
`POST` that fails part-way is never sent again, so a post is never made twice.
