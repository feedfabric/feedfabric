---
layout: ../../layouts/Doc.astro
title: Following and fan-out
description: The Following feed is a Redis timeline per member, filled when posts are written and read back through Postgres, with Postgres as the fallback.
---

## Events: the outbox and Kafka

Every change that other parts of the system care about is written with an `OutboxMessage` in the same database
transaction (`db.AddEvent(...)`). `FeedFabric.Worker.Events` then:

1. **Publishes the outbox.** `OutboxPublisher` takes up to 100 unpublished rows with `FOR UPDATE SKIP LOCKED` (so
   several workers can run), sends them to Kafka keyed by post or user ID, and marks them processed. When a batch
   isn't full it waits 500 ms.
2. **Consumes the topics**, each in its own consumer group so one can lag without holding up the others. Offsets are
   committed only after the handler succeeds; a failing handler retries with backoff up to 30 seconds apart.
   Handlers record what they've done in `ProcessedEvents`, so a redelivered event is a no-op.

| Event | Topic | Consumer group | What happens |
|---|---|---|---|
| `PostCreated` (posts and reposts) | `feedfabric.posts` | `worker-events` | Fan-out to Redis timelines |
| `PostCreated`, `PostUpdated` | `feedfabric.posts` | `worker-embeddings` | Embed the text with MiniLM into Redis |
| `PostLiked`, `PostUnliked` | `feedfabric.engagement` | `worker-engagement` | Likers set, author affinity, topic interest |
| `CommentCreated` | `feedfabric.engagement` | `worker-engagement` | Affinity and topic interest, weighted above a like |
| `MediaUploaded` | `feedfabric.media` | `worker-media` | Scan, sniff, rebuild and score a photo |

Kafka in development is a single KRaft node with no volume: the outbox in Postgres is the source of truth, so a fresh
broker loses nothing that matters. The worker creates missing topics on startup.

## Fan-out on write

`FeedFanOutHandler` takes a `PostCreated` event and adds the post to timelines:

- One Redis sorted set per member, `feed:{userId}`: member the post ID, score its creation time in Unix
  milliseconds.
- The author's own timeline first, then every follower's, 1,000 followers per batch (keyset paging over the follow
  table, so a popular author never loads all their followers at once).
- After each add, the timeline is trimmed to the newest `Feed:MaxLength` entries (500).

Reposts are posts, so they fan out the same way. Held posts fan out too; reading filters them (below).

<div class="note">

**Following someone doesn't backfill.** `PUT /follows/{externalUserId}` adds the follow; their earlier posts don't
enter your Redis timeline, only what they post from then on. Unfollowing doesn't remove what's already there either;
those entries age out as newer posts push them past 500.

</div>

## Reading the Following feed

```http
GET /userfeed/home?limit=20
GET /userfeed/home?limit=20&cursor=0193a6e2-…
```

`HomeFeedService` reads the timeline newest first from the cursor's position, then loads those posts from Postgres,
so text is never stale and anything that shouldn't be shown drops out: deleted posts, held posts (except the
viewer's own), posts by suspended accounts, and reposts of any of those. It reads in batches until it has a full page.

```json
{
  "posts": [ { "id": "…", "authorUsername": "maya_runs", "content": "…", "tags": [ … ], "media": [ … ], … } ],
  "nextCursor": "0193a6e2-…",
  "source": "redis"
}
```

- **`nextCursor`** is the last post's ID; pass it back for the next, older page. `null` means there's nothing older.
- **`source`** says where the page came from: `redis`, `postgres`, or `redis+postgres`.

### When Postgres serves it

The API starts and keeps working without Redis. Postgres builds the page straight from the follow graph (fan-out on
read: your own posts and those of everyone you follow, by ID, which for UUIDv7 IDs is creation order) when:

- Redis is down or errors,
- the member has no timeline yet (a new member, or Redis was flushed),
- the cursor isn't in the timeline (trimmed away, or it came from a Postgres page),
- or a page runs past the 500 entries Redis keeps, in which case it continues from Postgres (`redis+postgres`).

Both are newest first and page the same way, so a client never needs to know which one it got.

## Other newest-first lists

| Route | What |
|---|---|
| `GET /userfeed/favourites` | Posts by the member's [favourites](#favourites), from Postgres |
| `GET /userfeed/tags/{tag}/posts` | Posts with that hashtag or inferred topic |
| `GET /userfeed/mentions` | Posts and comments that @mention the member |
| `GET /userfeed` | The member's own posts, with `order` (`newest` or `oldest`) and `gt`/`gte`/`lt`/`lte` ID bounds |

All take `limit` (1 to 100, default 20) and `cursor` (the previous page's `nextCursor`). `GET /userfeed` returns the
bare post shape (no author name, tags or photos), the others the full feed post.

## Favourites

Up to 30 accounts a member never wants to miss (`PUT /favourites/{externalUserId}`; the 31st is
`409 favourite.limit_reached`). They needn't be followed. Favourites:

- have their own newest-first feed, `GET /userfeed/favourites`;
- are **always For You candidates**: their live posts from the last 48 hours (or the profile's look-back, if longer),
  up to 100, whatever the profile's sources;
- are lifted by the `favourite` boost, which every preset but *Newest first* has.

`GET /favourites` lists them, most recently added first, with username, display name and avatar.

## Logged as served

Following, favourites and For You pages are all written to the [feed log](/how-it-works/training/) with the same
ranking signals, so the ranker learns from all three and the dashboard can compare them. A repost is logged as the
post it shares, since that's what the reader sees and acts on. Tag and mention lists aren't logged.
