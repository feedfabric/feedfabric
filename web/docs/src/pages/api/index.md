---
layout: ../../layouts/Doc.astro
title: API reference
description: Every route the API serves, grouped by what it's for, with the parameters that matter and the error codes each can return.
---

## Conventions

- **Base URL**: wherever the API runs (`http://localhost:5161` with `make run`). Member routes are under `/userfeed`,
  `/follows`, `/favourites` and `/media`; staff routes under `/admin` and `/ranking`.
- **Auth**: every route needs `Authorization: Bearer <token>` (in Development, or `X-Dev-User: <id>`). Without one:
  `401 unauthorized`. Routes marked *moderator*, *ranking admin* or *staff* also need that role outside Development:
  `403 forbidden` without it. See [Sign-in and users](/auth/). Your own servers can instead send an
  [API key](/sdks/) (`Bearer ffk_…`) with `FeedFabric-User: <externalUserId>` to act for a member; keys reach every
  member route and no staff one.
- **JSON** in and out, camelCase, `Content-Type: application/json` on bodies (`415 unsupported_media_type`
  otherwise). IDs are UUID strings; times are ISO 8601 UTC.
- **Paging**: `limit` (1–100, default 20) and `cursor`, the previous page's `nextCursor`; `null` means the end.
- **Every write** by a suspended member is `403 user.suspended`, except the reading signals.
- **Every member route** can return `404 user.not_synced` if the caller's user doesn't exist (only when
  `Auth:ProvisionUsers` is off, or a first-request race), and `400 validation_failed` for a malformed ID, body or
  out-of-range parameter. These aren't repeated below.
- **Errors** are Problem Details with a stable `code`: see [Errors](/api/errors/).

### The OpenAPI document

In Development the API serves its OpenAPI document at **`/openapi/v1.json`**
([`http://localhost:5161/openapi/v1.json`](http://localhost:5161/openapi/v1.json)); import it into Postman, Bruno or
a client generator. The admin panel's *API reference* page (System → API reference) shows a saved copy, for environments
where the live document isn't served. These tables add what the document can't say: what each route is for, and the
error codes it returns.

## Feeds

| | Route | Purpose | Query | Errors |
|---|---|---|---|---|
| <span class="m">GET</span> | `/userfeed/home` | Following: posts from accounts you follow and your own, newest first | `limit`, `cursor` (a post ID) | |
| <span class="m">GET</span> | `/userfeed/foryou` | For You: ranked, each post with `ranking` | `limit`, `cursor` (an offset), `profile` | `404 profile.not_found` |
| <span class="m">GET</span> | `/userfeed/favourites` | Posts from your favourites, newest first | `limit`, `cursor` | |
| <span class="m">GET</span> | `/userfeed/tags/trending` | Tags on the most recent posts, hashtags and inferred topics alike: `[{ tag, posts }]` | `hours` (1–168, 24), `limit` (1–50, 10) | |
| <span class="m">GET</span> | `/userfeed/tags/{tag}/posts` | Posts with a tag, newest first | `limit`, `cursor` | |
| <span class="m">GET</span> | `/userfeed/mentions` | Posts and comments that @mention you, newest first: `{ mentions, nextCursor }` | `limit`, `cursor` | |
| <span class="m">GET</span> | `/userfeed` | Your own posts, in the bare post shape | `limit`, `cursor`, `order` (`newest`/`oldest`), `gt`, `gte`, `lt`, `lte` (post IDs), `type` (only posts with [data](/sdks/typed-data/) of that type) | |

Feed responses are `{ posts, nextCursor, source }` (Following, favourites, tags) or
`{ posts, nextCursor, ranker, profile, candidates }` (For You). Each post has `likes`, `comments` and `reposts`, and
`liked` and `reposted` for the signed-in member (on a repost, on its `repostOf`). See [Feeds and paging](/build-a-ui/feeds/) and the
[types](/build-a-ui/api-client/#types).

## Posts

| | Route | Purpose | Body | Success | Errors |
|---|---|---|---|---|---|
| <span class="m post">POST</span> | `/userfeed/posts` | Create a post | `{ content?, tags?, mediaIds?, data? }` | `201` post | `422 content.violation`, `400 post.too_many_media`, `404 media.not_found`, `409 media.not_ready`, `400 post.data_type_unknown`, `400 post.data_too_large`, `400 post.data_invalid` |
| <span class="m put">PUT</span> | `/userfeed/posts/{id}` | Edit, within 60 minutes | `{ content?, tags?, data? }` | `200` post | `404 post.not_found`, `403 post.not_author`, `409 post.edit_window_closed`, `409 post.is_repost`, `422 content.violation`, and the three `post.data_*` |
| <span class="m delete">DELETE</span> | `/userfeed/posts/{id}` | Delete your post | | `204` | `404 post.not_found`, `403 post.not_author` |

The post is `{ id, authorId, content, createdAt, editedAt, repostOfId, isHeld, data }`. There's no route to fetch one post.

`content` is required unless the post has `data`. `tags` are your app's own (up to 10, written like hashtags without
the `#`) and show on the post with source `app`. `data` is `{ type, value }` of a registered data type, checked
against its schema, and comes back as `{ type, version, value }` on the post in every feed. On an edit, leaving out
`tags` or `data` keeps the post's. See [Your own data on posts](/sdks/typed-data/).

## Engagement

| | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| <span class="m put">PUT</span> | `/userfeed/posts/{id}/like` | Like; repeating is a no-op | `204` | `404 post.not_found` (also for a repost's own ID) |
| <span class="m delete">DELETE</span> | `/userfeed/posts/{id}/like` | Unlike; repeating is a no-op | `204` | `404 post.not_found` |
| <span class="m put">PUT</span> | `/userfeed/posts/{id}/repost` | Repost (the original or a repost of it) | `204` | `404 post.not_found` |
| <span class="m delete">DELETE</span> | `/userfeed/posts/{id}/repost` | Undo your repost | `204` | `404 post.not_found` |
| <span class="m put">PUT</span> | `/follows/{externalUserId}` | Follow; repeating is a no-op | `204` | `404 user.not_found`, `400 follow.self` |
| <span class="m delete">DELETE</span> | `/follows/{externalUserId}` | Unfollow | `204` | `404 user.not_found`, `400 follow.self` |
| <span class="m">GET</span> | `/favourites` | Your favourites, newest first: `[{ externalUserId, username, displayName, avatarUrl, addedAt }]` | `200` | |
| <span class="m put">PUT</span> | `/favourites/{externalUserId}` | Add a favourite (up to 30) | `204` | `404 user.not_found`, `400 favourite.self`, `409 favourite.limit_reached` |
| <span class="m delete">DELETE</span> | `/favourites/{externalUserId}` | Remove a favourite | `204` | `404 user.not_found`, `400 favourite.self` |

## Comments

| | Route | Purpose | Body / query | Success | Errors |
|---|---|---|---|---|---|
| <span class="m">GET</span> | `/userfeed/posts/{id}/comments` | Top-level comments, oldest first | `limit`, `cursor` | `200 { comments, nextCursor }` | `404 post.not_found` |
| <span class="m">GET</span> | `/userfeed/comments/{id}/replies` | Direct replies to a comment | `limit`, `cursor` | `200 { comments, nextCursor }` | `404 comment.not_found` |
| <span class="m post">POST</span> | `/userfeed/posts/{id}/comments` | Comment, or reply with `parentCommentId` | `{ content, parentCommentId? }` | `201` comment | `404 post.not_found`, `404 comment.not_found` (parent), `422 content.violation` |
| <span class="m delete">DELETE</span> | `/userfeed/comments/{id}` | Delete your comment | | `204` | `404 comment.not_found`, `403 comment.not_author` |

## Reports

| | Route | Body | Success | Errors |
|---|---|---|---|---|
| <span class="m post">POST</span> | `/userfeed/posts/{id}/report` | `{ reason }`: `spam`, `harassment`, `hate`, `threat`, `sexual`, `self_harm` or `other` | `204` | `404 post.not_found` (also for reposts) |
| <span class="m post">POST</span> | `/userfeed/comments/{id}/report` | `{ reason }` | `204` | `404 comment.not_found` |

Reporting twice counts once; three different reporters hold the item for review
([Moderation](/how-it-works/moderation/#reports-from-members)).

## Reading signals

Accepted from suspended members too. See [Reading signals](/build-a-ui/signals/).

| | Route | Purpose | Body | Success | Errors |
|---|---|---|---|---|---|
| <span class="m post">POST</span> | `/userfeed/impressions` | Posts seen: at least half on screen for a second | `{ postIds }` (up to 100) | `202 { counted }` | |
| <span class="m post">POST</span> | `/userfeed/dwell` | Time on screen per view, sent as each scrolls away (60 s cap per view) | `{ views: [{ postId, ms }] }` (up to 100) | `202 { counted }` | |
| <span class="m post">POST</span> | `/userfeed/posts/{id}/open` | The member opened the post | | `202 { counted }` | |
| <span class="m post">POST</span> | `/userfeed/posts/{id}/not-interested` | Hide from For You for 30 days; less of its author and topics | | `204` | `404 post.not_found` |
| <span class="m post">POST</span> | `/userfeed/posts/{id}/show-more` | More of its author and topics | | `204` | `404 post.not_found` |

## Photos

| | Route | Purpose | Body | Success | Errors |
|---|---|---|---|---|---|
| <span class="m post">POST</span> | `/media/uploads` | An upload slot and signed URL | `{ contentType, size }` | `201 { mediaId, uploadUrl, contentType, expiresAt }` | `415 media.type_not_allowed`, `413 media.too_large` |
| <span class="m post">POST</span> | `/media/{id}/complete` | The file is uploaded; start the checks | | `202` media | `404 media.not_found`, `409 media.wrong_state` |
| <span class="m">GET</span> | `/media/{id}` | Status of your upload: `awaitingUpload`, `processing`, `ready` or `rejected` | | `200` media | `404 media.not_found` |
| <span class="m put">PUT</span> | `/blobs/{key}?expires=&sig=&contentType=` | Development only: the signed upload URL itself (local storage) | the file | `200` | `403` bad signature or type, `413` too large |
| <span class="m">GET</span> | `/blobs/{key}?expires=&sig=` | Development only: a signed read URL | | `200` the file | `403`, `404` |

The `/blobs` routes exist only with `BlobStorage:Provider` `Local`; you never build their URLs yourself, the API
hands them out. See [Photos](/how-it-works/media/) and [Composing with photos](/build-a-ui/compose/).

## Ranking profiles <small>(ranking admin)</small>

| | Route | Purpose | Errors |
|---|---|---|---|
| <span class="m">GET</span> | `/ranking/profiles` | Presets first, then saved profiles at their latest version | |
| <span class="m">GET</span> | `/ranking/profiles/{key}` | One profile: `{ key, version, preset, label, isDefault, settings }` | `404 profile.not_found` |
| <span class="m">GET</span> | `/ranking/profiles/{key}/versions` | A saved profile's versions (empty for a preset) | `404 profile.not_found` |
| <span class="m">GET</span> | `/ranking/profiles/{key}/versions/{n}` | One version | `404 profile.not_found` |
| <span class="m put">PUT</span> | `/ranking/profiles/{key}` | Create, or save a new version; body is the settings | `409 profile.read_only`, `400 profile.invalid_key`, `400 profile.invalid` |
| <span class="m delete">DELETE</span> | `/ranking/profiles/{key}` | Delete a saved profile | `409 profile.read_only`, `404 profile.not_found` |
| <span class="m post">POST</span> | `/ranking/preview?viewer=&limit=` | The For You these settings would give `viewer` (default you; `limit` up to 50), without saving or logging | `400 profile.invalid`, `404 user.not_found` |
| <span class="m">GET</span> | `/ranking/vocabulary` | Variables, functions, operators, goals, presets and defaults, for editors | |

See [Ranking profiles](/how-it-works/profiles/).

## Topics <small>(ranking admin)</small>

| | Route | Purpose | Errors |
|---|---|---|---|
| <span class="m">GET</span> | `/admin/topics` | The set in use: `{ source, version, library, savedAt, savedBy, topics }`; `source` is `saved`, `configuration`, `default` or `off` | |
| <span class="m">GET</span> | `/admin/topics/library` | Ready-made sets to start from: `[{ key, name, description, keywords }]` | |
| <span class="m">GET</span> | `/admin/topics/versions` | Every saved set, newest first: `[{ version, savedAt, savedBy, library, topics }]`, `topics` being how many | |
| <span class="m">GET</span> | `/admin/topics/versions/{n}` | One saved set: topic name to keywords | `404 topics.version_not_found` |
| <span class="m put">PUT</span> | `/admin/topics` | Save `{ topics, library? }` as a new version and use it; empty `topics` means hashtags only | `400 topics.invalid`, `400 topics.unknown_library` |
| <span class="m post">POST</span> | `/admin/topics/preview` | The topics `{ text, topics? }` would get, from the draft `topics` or the set in use: `[{ topic, confidence }]` | `400 topics.invalid` |

New and edited posts use a saved set within a minute; posts already tagged keep their tags. See
[Tags and mentions](/how-it-works/posting/#tags-and-mentions).

## Moderation <small>(moderator)</small>

| | Route | Purpose | Errors |
|---|---|---|---|
| <span class="m">GET</span> | `/admin/moderation/overview` | What's waiting; the last day's blocks, holds, reports and decisions | |
| <span class="m">GET</span> | `/admin/moderation/queue` | Held and flagged posts and comments, oldest first | |
| <span class="m">GET</span> | `/admin/moderation/log?action=&limit=` | Latest events; `action` `blocked`, `held`, `reported` or `reviewed`; `limit` 1–500 (100) | |
| <span class="m">GET</span> | `/admin/moderation/reports?limit=` | Latest member reports, newest first; `limit` 1–500 (100) | |
| <span class="m post">POST</span> | `/admin/moderation/posts/{id}/decision` | `{ "decision": "approved" \| "removed" }` | `404 post.not_found` |
| <span class="m post">POST</span> | `/admin/moderation/comments/{id}/decision` | `{ "decision": "approved" \| "removed" }` | `404 comment.not_found` |
| <span class="m">GET</span> | `/admin/users?q=&status=&sort=&offset=&limit=` | Search users; `status` `active`/`suspended`; `sort` `username`, `posts`, `reports` or `flags` | |
| <span class="m">GET</span> | `/admin/users/{externalUserId}` | A user for staff | `404 user.not_found` |
| <span class="m put">PUT</span> | `/admin/users/{externalUserId}/status` | `{ "status": "suspended" \| "active" }`, recorded in the log | `404 user.not_found` |
| <span class="m put">PUT</span> | `/admin/users/{externalUserId}/verified` | `{ "verified": true }` | `404 user.not_found` |
| <span class="m">GET</span> | `/admin/posts?q=&author=&status=&reported=&offset=&limit=` | Posts in every state; `status` `visible`, `held`, `removed` or `deleted` | |
| <span class="m">GET</span> | `/admin/posts/{id}` | A post with its comments, reports and moderation history | `404 post.not_found` |
| <span class="m">GET</span> | `/admin/media/{id}` | An upload with its nudity score and the line it's judged against | `404 media.not_found` |

## Data types

| | Route | Purpose | Errors |
|---|---|---|---|
| <span class="m">GET</span> | `/data-types` | The registered types: `[{ key, label, description, version, schema, updatedAt, updatedBy }]` | |
| <span class="m">GET</span> | `/data-types/{key}` | One type | `404 data_type.not_found` |
| <span class="m">GET</span> | `/data-types/{key}/schema` | Its JSON Schema alone (`application/schema+json`), for code generators | `404 data_type.not_found` |

## Integrations <small>(integration admin)</small>

| | Route | Purpose | Body | Errors |
|---|---|---|---|---|
| <span class="m">GET</span> | `/admin/api-keys` | Every key, newest first, revoked ones too: `[{ id, name, prefix, createdAt, createdBy, lastUsedAt, revokedAt, revokedBy }]` | | |
| <span class="m post">POST</span> | `/admin/api-keys` | Make a key: `201` with the same fields plus `key`, shown only this once | `{ name }` | |
| <span class="m delete">DELETE</span> | `/admin/api-keys/{id}` | Revoke a key: at once here, within a minute on other servers | | `404 api_key.not_found` |
| <span class="m put">PUT</span> | `/admin/data-types/{key}` | Add or change a data type. The version goes up only when the schema changes | `{ label, description?, schema }` | `400 data_type.invalid_key`, `400 data_type.invalid_schema` |
| <span class="m delete">DELETE</span> | `/admin/data-types/{key}` | Remove a data type; posts keep their data | | `404 data_type.not_found` |

API keys can't use these, or any other staff route.

## Staff <small>(moderator, ranking admin or integration admin)</small>

| | Route | Purpose |
|---|---|---|
| <span class="m">GET</span> | `/admin/me` | Any signed-in user: who they are and which roles they have (`moderator`, `rankingAdmin`, `integrationAdmin`, `environment`) |
| <span class="m">GET</span> | `/admin/dashboard?minutes=60` | Everything the admin panel's overview shows, in one call |
| <span class="m">GET</span> | `/admin/system` | Sign-in settings (no secrets), moderation thresholds, media limits, models, ranker, users by status, outbox and consumers |
| <span class="m">GET</span> | `/admin/feed/{externalUserId}/following` | A member's Following as they'd see it, without logging it |
| <span class="m">GET</span> | `/admin/feed/{externalUserId}/foryou?profile=` | A member's For You in a session of its own, without logging it |

On the two `/admin/feed` routes and `/ranking/preview`, `liked` and `reposted` are yours, not the member's: they're for
acting on the posts as yourself.

## Other

| | Route | Purpose |
|---|---|---|
| <span class="m">GET</span> | `/me` | The caller's `externalUserId`; a quick token check |
| <span class="m">GET</span> | `/openapi/v1.json` | The OpenAPI document, Development only |
