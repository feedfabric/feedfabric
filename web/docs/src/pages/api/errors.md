---
layout: ../../layouts/Doc.astro
title: Errors
description: Every error is RFC 9457 Problem Details with a stable code. The codes, their statuses and params, and which routes return them.
---

## The shape

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "Posts can only be edited within 60 minutes of posting.",
  "code": "post.edit_window_closed",
  "params": { "editWindowMinutes": 60 },
  "traceId": "00-…"
}
```

- **`code`** is always present and never renamed. Match on it, and show your own (translated) message for it.
- **`params`** holds the values a message needs, when there are any.
- **`errors`** lists field problems on `validation_failed` and `profile.invalid`.
- **`detail`** is English for developers and logs. Don't show it to members or match on it.
- **`traceId`** is what the server's logs are keyed on; quote it when reporting a `500`.

A `429` or `503` is temporary: wait for `Retry-After` and try again, and tell the member only if it keeps happening.

New codes may be added, so always fall back to a generic message for one you don't recognise. The codes are defined
in `src/FeedFabric.Application/Errors/ErrorCodes.cs`; the repository's `docs/errors.md` is the canonical list.

## Codes

| Code | Status | Params | Meaning | Returned by |
|---|---|---|---|---|
| `validation_failed` | 400 | | See `errors` below | Any route with a body or bounded parameters |
| `user.not_synced` | 404 | `externalUserId` | The signed-in user has no FeedFabric user yet | Member routes |
| `user.suspended` | 403 | | Suspended: can read, but not post, comment, like, repost, follow, report or upload | Every member write except reading signals |
| `post.not_found` | 404 | | No such post, deleted, or a repost's own ID where an original is needed | Posts, likes, reposts, comments, reports, *Not interested*, *Show more*, moderation |
| `post.not_author` | 403 | | Only the author can edit or delete a post | `PUT`/`DELETE /userfeed/posts/{id}` |
| `post.edit_window_closed` | 409 | `editWindowMinutes` | The 60-minute edit window has passed | `PUT /userfeed/posts/{id}` |
| `post.is_repost` | 409 | | Reposts can't be edited | `PUT /userfeed/posts/{id}` |
| `post.too_many_media` | 400 | | More photos than `Media:MaxPerPost` (4) | `POST /userfeed/posts` |
| `comment.not_found` | 404 | | No such comment, or deleted; also a missing parent comment | Comments, replies, comment reports, moderation |
| `comment.not_author` | 403 | | Only the author can delete a comment | `DELETE /userfeed/comments/{id}` |
| `content.violation` | 422 | `category` | Breaks the community rules: `threat`, `blocked_term`, `link_spam`, `flooding`… | Creating and editing posts, commenting |
| `media.not_found` | 404 | | No such upload, not yours, or already on another post | `/media/*`, `POST /userfeed/posts` |
| `media.not_ready` | 409 | | A photo hasn't finished its checks, or was rejected | `POST /userfeed/posts` |
| `media.type_not_allowed` | 415 | `allowed` | Only JPEG, PNG and WebP | `POST /media/uploads` |
| `media.too_large` | 413 | | Over `Media:MaxUploadBytes` | `POST /media/uploads` |
| `media.wrong_state` | 409 | | The upload was already completed | `POST /media/{id}/complete` |
| `user.not_found` | 404 | `externalUserId` | No user with that ID | Follows, favourites, `/ranking/preview`, `/admin/users/*` |
| `follow.self` | 400 | | Can't follow yourself | `/follows/{id}` |
| `favourite.self` | 400 | | Can't favourite yourself | `/favourites/{id}` |
| `favourite.limit_reached` | 409 | `max` | Already at 30 favourites | `PUT /favourites/{id}` |
| `profile.not_found` | 404 | `profile` | No ranking profile with that key | `/userfeed/foryou?profile=`, `/ranking/profiles/*` |
| `profile.read_only` | 409 | `profile` | Presets can't be changed or deleted | `PUT`/`DELETE /ranking/profiles/{key}` |
| `profile.invalid` | 400 | | Settings out of range, or a formula that doesn't parse; `errors` lists `{ field, message, position? }` | `PUT /ranking/profiles/{key}`, `/ranking/preview` |
| `profile.invalid_key` | 400 | `profile` | Keys are 2–40 lowercase letters, digits and dashes, starting with a letter | `PUT /ranking/profiles/{key}` |
| `bad_request` | 400 | | Generic fallback | |
| `unauthorized` | 401 | | Missing or invalid token | Every route |
| `forbidden` | 403 | | Missing role (generic fallback) | Staff routes |
| `not_found` | 404 | | Unknown route | |
| `method_not_allowed` | 405 | | | |
| `conflict` | 409 | | Generic fallback | |
| `unsupported_media_type` | 415 | | Send `Content-Type: application/json` | Routes with a body |
| `request_too_large` | 413 | | The body is over 1 MB (`Kestrel:Limits:MaxRequestBodySize`) | Routes with a body |
| `rate_limited` | 429 | `retryAfterSeconds` | Too many requests of one kind; wait that long (also in `Retry-After`) | Writes, signals, `/ranking/preview`, `/me/export`, `DELETE /me` |
| `internal_error` | 500 | | Unexpected server error, including a safety model that couldn't run; quote `traceId` | Any |
| `service_unavailable` | 503 | `retryAfterSeconds` | Postgres, Valkey or blob storage can't be reached for now; try again after `Retry-After` | Any route that needs one |

## Validation errors

```json
{
  "status": 400,
  "code": "validation_failed",
  "errors": [
    { "field": "content", "code": "required" },
    { "field": "limit", "code": "out_of_range", "params": { "min": 1, "max": 100 } }
  ]
}
```

`field` is the camelCase field or parameter name, left out when the problem is the whole body (malformed JSON, say).

| Field code | Params | Meaning |
|---|---|---|
| `required` | | Missing or blank |
| `out_of_range` | `min`, `max` | Outside the allowed range (also too many items: `max` only) |
| `too_long` | `max` | More characters than allowed: 5,000 for a post, 2,000 for a comment |
| `invalid_value` | | Wrong format or type, or not one of the allowed values (a malformed ID, an unknown report reason) |

## Not Problem Details

Two kinds of response come from elsewhere and have no body to parse:

- **Signed blob URLs in development** (`/blobs/…`): `403` for a bad or expired signature or the wrong `Content-Type`,
  `413` for too large, `404` for a missing file.
- **S3** (presigned URLs): the bucket's own XML errors.
