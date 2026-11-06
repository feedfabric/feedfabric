# API errors

Every error response is [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with two additions:

- `code`: always present and stable. Clients show their own translated message for it.
- `params`: values the message needs, when there are any.

`detail` is English text for developers and logs. Don't show it to users or match on it.

```json
{
  "status": 409,
  "title": "Conflict",
  "detail": "Posts can only be edited within 60 minutes of posting.",
  "code": "post.edit_window_closed",
  "params": { "editWindowMinutes": 60 },
  "traceId": "00-…"
}
```

| Code | Status | Params | Meaning |
|---|---|---|---|
| `validation_failed` | 400 | | See `errors` below |
| `user.not_synced` | 404 | `externalUserId` | The signed-in user has no local user record yet |
| `user.suspended` | 403 | | The account is suspended: it can read, but not post, comment, like, repost, follow, report or upload |
| `post.not_found` | 404 | | No such post, or it was deleted. Liking or commenting on a repost's own ID also gives this; use the original's |
| `post.not_author` | 403 | | Only the author can edit or delete a post |
| `post.edit_window_closed` | 409 | `editWindowMinutes` | The edit window has passed |
| `post.is_repost` | 409 | | Reposts can't be edited |
| `post.too_many_media` | 400 | | Too many images on one post |
| `comment.not_found` | 404 | | No such comment, or it was deleted |
| `comment.not_author` | 403 | | Only the author can edit or delete a comment |
| `content.violation` | 422 | `category` | The post or comment breaks the community rules, e.g. `threat`, `insult`, `link_spam` |
| `media.not_found` | 404 | | No such upload, it isn't yours, or it's already on another post |
| `media.not_ready` | 409 | | An image hasn't finished scanning, or was rejected |
| `media.type_not_allowed` | 415 | `allowed` | Only JPEG, PNG and WebP can be uploaded |
| `media.too_large` | 413 | | The file is over the size limit |
| `media.wrong_state` | 409 | | The upload has already been completed |
| `profile.not_found` | 404 | `profile` | No ranking profile with that key |
| `profile.read_only` | 409 | `profile` | Presets can't be changed or deleted; save a copy under a new key |
| `profile.invalid` | 400 | | Settings out of range or a formula that doesn't parse; `errors` lists each `{ field, message, position? }` |
| `profile.invalid_key` | 400 | `profile` | Keys are 2–40 lowercase letters, digits and dashes, starting with a letter |
| `user.not_found` | 404 | `externalUserId` | No user with that ID, e.g. when following |
| `follow.self` | 400 | | Users can't follow themselves |
| `favourite.self` | 400 | | Users can't make themselves a favourite |
| `favourite.limit_reached` | 409 | `max` | Already at the most favourites allowed (30); remove one to add another |
| `bad_request` | 400 | | Generic fallback |
| `unauthorized` | 401 | | Missing or invalid token |
| `forbidden` | 403 | | Generic fallback |
| `not_found` | 404 | | Unknown route |
| `method_not_allowed` | 405 | | |
| `conflict` | 409 | | Generic fallback |
| `unsupported_media_type` | 415 | | Send `Content-Type: application/json` |
| `internal_error` | 500 | | Unexpected server error; quote `traceId` when reporting it |

`validation_failed` lists each problem in `errors`. `field` is the camelCase field name; it is left out when the problem is the whole request body.

```json
{
  "code": "validation_failed",
  "errors": [
    { "field": "content", "code": "required" },
    { "field": "limit", "code": "out_of_range", "params": { "min": 1, "max": 100 } }
  ]
}
```

| Field code | Params | Meaning |
|---|---|---|
| `required` | | Missing or blank |
| `out_of_range` | `min`, `max` | Outside the allowed range |
| `invalid_value` | | Wrong format or type, e.g. a malformed ID or JSON |

Codes are defined in `src/FeedFabric.Application/Errors/ErrorCodes.cs`. Codes are never renamed; new ones may be added, so clients should fall back to a generic message for codes they don't recognise.
