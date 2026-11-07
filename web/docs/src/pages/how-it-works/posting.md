---
layout: ../../layouts/Doc.astro
title: Posting and safety
description: What happens between a member pressing Post and the post reaching anyone's feed, and the same for comments, edits and reposts.
---

## Creating a post

```http
POST /userfeed/posts
Content-Type: application/json

{ "content": "5k along the canal before work #running", "mediaIds": ["0193…"] }
```

`content` is required (a photo needs a caption); `mediaIds` are uploads that have [passed their checks](/how-it-works/media/),
in display order, at most `Media:MaxPerPost` (4). `PostService.CreateAsync` then, in order:

1. **Checks the text** with `TextModerator` (below). A block stops here: `422 content.violation`, nothing saved.
2. **Checks the photos**: each must be the author's own, `ready`, and not already on another post
   (`media.not_found`, `media.not_ready`, `post.too_many_media` otherwise).
3. **Saves in one transaction**: the post (status `Visible`, or `Held`), a moderation event if the text was held or
   flagged, its tags, its photos, its @mentions, and a `PostCreated` row in the outbox.

The response is `201` with the post:

```json
{ "id": "0193…", "authorId": "0192…", "content": "5k along the canal…", "createdAt": "2026-11-06T08:12:03Z",
  "editedAt": null, "repostOfId": null, "isHeld": false }
```

Writing the outbox row in the same `SaveChanges` as the post is what makes the rest reliable: the event exists exactly
when the post does, and the worker turns it into timeline entries and an embedding
([Following and fan-out](/how-it-works/following/)).

## The text checks

`TextModerator` runs on every post, edit and comment. Rules first, then the offensive-language model for what rules
can't see. Blocking wins over holding, holding over flagging.

| Check | Default | Outcome |
|---|---|---|
| Blocked terms (`Moderation:BlockedTerms`), whole words, any case | empty | **Block**, category `blocked_term` |
| More than `Moderation:MaxLinks` links | 2 | **Block**, `link_spam` |
| A character repeated `Moderation:MaxRepeatedCharacters` times | 12 | **Block**, `flooding` |
| Toxicity `threat` ≥ `ToxicityRejectAt` | 0.6 | **Block**, `threat` |
| `severe_toxicity`, `identity_attack`, `sexual_explicit` ≥ `ToxicityHoldAt` | 0.5, 0.6, 0.9 | **Hold** |
| `insult` ≥ `ToxicityFlagAt`, only when the text speaks to someone | 0.9 | **Flag** |

- **Block**: refused with `422 content.violation` and `params.category`, and recorded in the moderation log.
- **Hold**: saved, but only its author sees it (`isHeld: true`) until a moderator approves or removes it.
- **Flag**: published as normal, and put in the review queue.

"Speaks to someone" is a second-person pattern (*you*, *your*, *u*, *ur*, *ya* and friends): "you're an idiot" is
flagged, "grinning like an idiot" isn't. The checks **fail closed**: if the classifier can't run (no model, a crash),
the request fails and nothing is saved. Run `make models` before posting.

<div class="why">

**For your UI:** show `content.violation` as "this breaks the community rules" with your own wording for the
category, and keep the member's text so they can change it. Show a held post (`isHeld: true`) to its author with a
label such as "Only you can see this while it's reviewed"; nobody else will ever be served it.

</div>

## Tags and mentions

Each post gets tags of two kinds, returned on feed posts as `{ "tag", "source" }`:

- **`hashtag`**: `#words` in the text, lowercased. A hashtag starts with a letter, so `#1` and `#5k` stay plain text.
- **`inferred`**: topics from keyword rules, each with a confidence. Each community sets its own: ranking admins
  save them through [`/admin/topics`](/api/#topics-ranking-admin), starting from a ready-made set (wellness,
  climbing, running, cycling or gardening) if they like. Until someone does, `Topics:Keywords` applies, and without
  that the built-in wellness topics (running, walking, travel, yoga, strength, cycling, swimming and more). A hashtag
  wins over an inferred topic of the same name.

Tags drive the topic scores For You uses, `GET /userfeed/tags/trending` and `GET /userfeed/tags/{tag}/posts`.
`@username` mentions of real users are stored with the post or comment and listed for the mentioned user by
`GET /userfeed/mentions`.

## Editing and deleting

- `PUT /userfeed/posts/{id}` with `{ "content" }` edits the text **within 60 minutes** of posting
  (`409 post.edit_window_closed` with `params.editWindowMinutes` after that). Only the author can
  (`403 post.not_author`), and reposts can't be edited (`409 post.is_repost`). Photos can't be changed.
- An edit is checked again. If the new text needs a look, a visible post is held or flagged again, even if a moderator
  approved the old text; a held post stays held. Tags and mentions are worked out afresh, and a `PostUpdated` event
  re-embeds it.
- `DELETE /userfeed/posts/{id}` soft-deletes it (`204`). Timelines still hold its ID, but every feed drops deleted
  posts as it reads.

## Comments

```http
POST /userfeed/posts/{postId}/comments
{ "content": "Lovely route!", "parentCommentId": null }
```

Comments go through the same text checks (block, hold, flag) with the same error. Set `parentCommentId` to reply to
a comment on the same post. `GET /userfeed/posts/{postId}/comments` lists top-level comments **oldest first**, each
with a `replyCount`; load replies with `GET /userfeed/comments/{commentId}/replies`. Both page with `limit` and
`cursor`.

A deleted or removed comment still appears, with `isDeleted: true` and `content: null`, while it has replies to hold
the thread together. A held comment is shown only to its author (`isHeld: true`). Comments on a held post can only be
read and written by its author.

Commenting writes a `CommentCreated` event, which the engagement worker turns into affinity for the post's author
and interest in its topics. A held comment only counts once a moderator approves it.

## Reposts

`PUT /userfeed/posts/{id}/repost` creates a post row with no content and `repostOfId` set, so fan-out puts it in
followers' timelines like any other post. Repeating it changes nothing (one live repost per member per post), and
`DELETE` undoes it. Either the original's ID or a repost's ID works; both resolve to the original.

In feeds a repost carries the original in `repostOf`; the repost's own `authorUsername` is who reposted it and its
`createdAt` is when. **Act on the original** (`repostOf.id`): liking, commenting and reporting return
`post.not_found` for a repost's own ID, and impressions, dwell and opens should name the original too, since that's
the post the feed log recorded as served.

## Suspended members

A suspended member can still read, but every write (post, comment, like, repost, follow, favourite, report, upload)
is refused with `403 user.suspended`, in one place (`SuspendedUsers` middleware). Reading signals (impressions,
dwell, opens, *Not interested*, *Show more*) are still accepted, since they're part of reading. A suspension takes
effect within 30 seconds on every API instance, at once on the one that made it.
