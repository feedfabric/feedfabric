# Data model

The fields for every entity in the system, grouped by the service that owns it.

Types are C# types. All internal IDs are `Guid` (UUIDv7, time-ordered), stored as Postgres `uuid` and sent as strings in the API. Users from the external API keep their original ID in `User.ExternalUserId`; everything else references the internal `User.Id`.

## IDs

Every entity inherits `Id` from `EntityBase`. Leave it empty when creating an entity: the Npgsql EF provider fills in a UUIDv7 on `Add`, before `SaveChanges`, so the ID can go straight into an outbox event in the same save.

UUIDv7 (RFC 9562) starts with a millisecond timestamp followed by random bits:

- **Globally unique without coordination**, so any instance or shard can create IDs.
- **Roughly time-ordered**, so new rows append to the end of the B-tree index instead of fragmenting it.
- **Not ordered within one millisecond.** Sorting by `Id` is stable and fine for cursors; where exact creation order matters, sort by `CreatedAt` then `Id`.
- **Reveals creation time** to anyone who can see the ID.

IDs from other systems are kept as `string`, even when that system uses `int` or `long`, so a format change on their side never needs a migration here. Convert them with `ToString(CultureInfo.InvariantCulture)` so the same value always produces the same key.


---

## Users

Local projection of users from the external API.

### User

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | Internal key |
| ExternalUserId | `string` | ID from the external API, unique index. String even though the source may be `int` |
| Username | `string` | Unique index |
| DisplayName | `string?` | |
| AvatarUrl | `string?` | |
| Status | `enum` | Active, Suspended, Deleted |
| IsVerified | `bool` | Useful ranking feature |
| CreatedAt | `DateTime` | When first seen in this system |
| SyncedAt | `DateTime` | Last refresh from the external API, for staleness checks |

---

## Posts service

### Post

Replies, quotes and reposts all live in this table.

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | Roughly time-ordered, used as pagination cursor |
| AuthorId | `Guid` | |
| Type | `enum` | Original, Reply, Quote, Repost |
| Content | `string` | Empty for plain reposts |
| ParentPostId | `Guid?` | Post being replied to |
| RootPostId | `Guid?` | Top of the thread |
| QuotedPostId | `Guid?` | For quotes |
| RepostOfId | `Guid?` | For reposts. One live repost per user per post (unique filtered index); undoing one soft-deletes it |
| LikeCount | `int` | Denormalized counter |
| ReplyCount | `int` | |
| RepostCount | `int` | |
| QuoteCount | `int` | |
| ViewCount | `long` | Flushed in batches from impressions |
| Language | `string?` | Detected, useful for filtering and ranking |
| Visibility | `enum` | Public, FollowersOnly |
| ModerationStatus | `enum` | Pending, Approved, Limited, Removed |
| Source | `string?` | Which client or API key created it |
| CreatedAt | `DateTime` | |
| EditedAt | `DateTime?` | |
| DeletedAt | `DateTime?` | Soft delete |
| Version | `int` | Concurrency token |

### PostMedia

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | |
| PostId | `Guid` | |
| Type | `enum` | Image, Video, Gif |
| Url | `string` | Blob storage URL |
| ThumbnailUrl | `string?` | Filled by background processing |
| Width | `int?` | |
| Height | `int?` | |
| DurationMs | `int?` | Video only |
| AltText | `string?` | |
| ProcessingStatus | `enum` | Pending, Ready, Failed |
| SortOrder | `int` | |

### PostMention

Split out so notifications and topic features don't need to parse text.

| Field | Type | Notes |
|---|---|---|
| PostId | `Guid` | |
| MentionedUserId | `Guid` | |

### PostHashtag

| Field | Type | Notes |
|---|---|---|
| PostId | `Guid` | |
| Tag | `string` | Normalized lowercase |

---

## Engagement service

### Like

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | Composite key with PostId |
| PostId | `Guid` | Index on its own for "who liked this" |
| CreatedAt | `DateTime` | |
| Source | `string?` | Client or API key, lets you discount bot traffic |

### Bookmark

Same shape as Like. A strong private signal for ranking.

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | Composite key with PostId |
| PostId | `Guid` | |
| CreatedAt | `DateTime` | |
| Source | `string?` | |

### NegativeFeedback

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | |
| UserId | `Guid` | |
| PostId | `Guid?` | |
| TargetUserId | `Guid?` | For "show less from this account" |
| Type | `enum` | NotInterested, ShowLessFromAuthor, ShowLessOfTopic |
| CreatedAt | `DateTime` | |

### Report

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | |
| ReporterId | `Guid` | |
| PostId | `Guid?` | |
| ReportedUserId | `Guid?` | |
| Reason | `enum` | Spam, Abuse, Misinformation, and so on |
| Details | `string?` | |
| Status | `enum` | Open, Reviewed, Actioned, Dismissed |
| CreatedAt | `DateTime` | |
| ResolvedAt | `DateTime?` | |

### Impression

High volume: keep it in an analytics store (e.g. ClickHouse) or Kafka, not the main database.

| Field | Type | Notes |
|---|---|---|
| ViewerId | `Guid` | |
| PostId | `Guid` | |
| RequestId | `Guid` | Ties it to a served timeline |
| Position | `int` | Rank in the feed when shown |
| CandidateSource | `string` | InNetwork, GraphTraversal, Embedding, and so on |
| Score | `float` | Ranker output at serve time |
| DwellMs | `int?` | Time on screen |
| Clicked | `bool` | |
| ShownAt | `DateTime` | |

---

## Social Graph service

### Follow

| Field | Type | Notes |
|---|---|---|
| FollowerId | `Guid` | Composite key with FolloweeId |
| FolloweeId | `Guid` | Separate index for follower lists |
| CreatedAt | `DateTime` | |

### Block

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | Composite key with TargetUserId |
| TargetUserId | `Guid` | |
| CreatedAt | `DateTime` | |

### Mute

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | Composite key with TargetUserId |
| TargetUserId | `Guid` | |
| CreatedAt | `DateTime` | |
| ExpiresAt | `DateTime?` | For temporary mutes |

### UserStats

Denormalized, updated from events.

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | |
| FollowerCount | `int` | |
| FollowingCount | `int` | |
| PostCount | `int` | |
| UpdatedAt | `DateTime` | |

---

## Recommendation data

### UserAffinity

The Real Graph equivalent.

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | Composite key with TargetUserId |
| TargetUserId | `Guid` | |
| Score | `float` | Predicted engagement likelihood |
| InteractionCount | `int` | Recent likes, replies, clicks combined |
| LastInteractionAt | `DateTime` | For time decay |
| UpdatedAt | `DateTime` | |

### UserEmbedding / PostEmbedding

| Field | Type | Notes |
|---|---|---|
| UserId / PostId | `Guid` | |
| Vector | `float[]` | Stored in the vector index |
| ModelVersion | `string` | Never compare vectors from different models |
| UpdatedAt | `DateTime` | |

### UserInterest

Topic or cluster membership (the SimClusters idea).

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | |
| TopicId | `int` | Cluster or topic |
| Weight | `float` | |
| UpdatedAt | `DateTime` | |

### FeedEntry

Stored in Redis as one sorted set per user; this is the logical shape.

| Field | Type | Notes |
|---|---|---|
| UserId | `Guid` | The key, e.g. `feed:{userId}` |
| PostId | `Guid` | Member |
| Score | `double` | Sort value, usually the post's CreatedAt as a Unix timestamp |
| AuthorId | `Guid` | Optional, makes unfollow cleanup easier |

---

## Notifications service

### Notification

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | |
| RecipientId | `Guid` | Index with CreatedAt |
| Type | `enum` | Like, Reply, Follow, Mention, Repost, Quote |
| ActorId | `Guid` | Who triggered it |
| PostId | `Guid?` | |
| GroupKey | `string?` | Collapses "Alice and 12 others liked your post" |
| IsRead | `bool` | |
| CreatedAt | `DateTime` | |

---

## Messaging infrastructure

Used by every service.

### OutboxMessage

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` | Becomes the event ID |
| Type | `string` | e.g. `PostCreated` |
| Payload | `string` | Serialized event |
| PartitionKey | `string` | PostId or UserId |
| CreatedAt | `DateTime` | |
| ProcessedAt | `DateTime?` | Null until published |
| Attempts | `int` | |
| LastError | `string?` | |

### ProcessedEvent

Consumer side, for idempotency.

| Field | Type | Notes |
|---|---|---|
| EventId | `Guid` | Composite key with Consumer; skip events already handled |
| Consumer | `string` | Which handler processed it, so each handler tracks its own progress |
| ProcessedAt | `DateTime` | |

### Event envelope

Every event published to Kafka carries these common fields, so one user action can be traced across every service it touches.

| Field | Type | Notes |
|---|---|---|
| EventId | `Guid` | Matches OutboxMessage.Id |
| Type | `string` | |
| Version | `int` | Schema version |
| OccurredAt | `DateTime` | |
| ActorId | `Guid` | User who caused the event |
| Source | `string?` | Client or API key |
| CorrelationId | `string` | Traces one action across services |

### Event flow

1. A service writes its change and an `OutboxMessage` in the same `SaveChanges` (`db.AddEvent(...)`), so the event exists exactly when the change does.
2. `FeedFabric.Worker.Events` polls unpublished rows with `FOR UPDATE SKIP LOCKED`, publishes each to Kafka and sets `ProcessedAt`. The payload is the envelope above with the event's data in `data`; the Kafka key is `PartitionKey` and the `type` header is `Type`.
3. The same worker consumes the topic (group `worker-events`) and runs the handlers for each type, committing the offset only after they succeed. Failed handlers retry with backoff.

| Event | Topic | Key | Data | Handlers |
|---|---|---|---|---|
| `PostCreated` | `feedfabric.posts` | PostId | `postId`, `authorId`, `createdAt` | `feed-fan-out`: adds the post to the author's and every follower's `feed:{userId}` in Redis, trimmed to the newest 500 |
