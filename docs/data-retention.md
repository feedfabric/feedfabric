# Data retention

What FeedFabric keeps about members, for how long, and what deleting an account leaves behind. Members write about
pregnancy, injuries, sleep and their bodies, so treat their content as likely special-category data under UK GDPR:
keep only what has a use, and shorten these periods rather than lengthen them.

The worker's retention job enforces the periods below. It runs every `Retention:Interval` (an hour), in one worker
instance at a time, deleting in small batches. Each period is a setting ([configuration.md](configuration.md#retention-worker)).

## What is kept, and for how long

| Data | Where | Kept for | Why |
|---|---|---|---|
| Profile, posts, comments, likes, follows, favourites, mentions | Postgres | Until the member deletes them or their account | It's the service |
| Photos (rebuilt images and thumbnails) | Blob storage | While their post exists | |
| Uploads never completed, ready photos never put on a post | Postgres, blob storage | 1 day (`Retention:AbandonedMedia`) | |
| Rejected uploads | Postgres (the file is deleted at once) | 30 days (`Retention:RejectedMedia`) | Explaining a rejection |
| Feed log: each post served, and what the member did with it | Postgres | 180 days (`Retention:FeedLog`) | Training and comparing rankers |
| Moderation events, once decided | Postgres | 365 days (`Retention:ModerationEvents`) | Repeat offenders, appeals |
| Moderation events still waiting for a moderator | Postgres | Until decided | The review queue |
| Reports | Postgres | While the reported post or comment exists | |
| Published outbox messages (copies of events) | Postgres | 7 days (`Retention:PublishedOutbox`) | Investigating a delivery |
| Parked outbox messages | Postgres | Until someone releases or deletes them ([runbooks.md](runbooks.md#parked-outbox-messages)) | |
| Record of events each consumer handled (IDs only) | Postgres | 35 days (`Retention:ProcessedEvents`) | Stops a redelivered event being applied twice |
| Events | Kafka | 7 days (`Kafka:Retention`) | Replay |
| Dead-lettered events | Kafka | 28 days (`Kafka:DeadLetterRetention`) | Waiting for a person |
| Home timelines (post IDs) | Redis | Newest 500 per member (`Feed:MaxLength`) | |
| Per-post impression and dwell signals | Redis | 30 days | Ranking |
| Impression and open totals per post | Redis | Posts up to 30 days old (`Retention:PostTotals`) | Ranking |
| What the ranker has learned about a member (authors and topics) | Redis | While the account exists | Ranking |
| "Not interested" and "show more" | Redis | 30 days | Ranking |
| Logs | Your log platform | Set it there; 90 days or less suggested | Investigations |
| Staff audit log (`FeedFabric.Audit` logs) | Your log platform | 365 days suggested, as long as the moderation events | Who changed what |
| Database and Redis backups | Your platform | Set it there; 35 days or less suggested, so deletions take effect in backups too | Recovery |

## Subject access

A member downloads everything held about them from `GET /me/export`: their profile, posts, comments, likes, follows,
favourites, reports they made, photos (with links valid for an hour), the moderation events about their content, the
feed log, and what the ranker has learned about them. It is limited to 5 an hour (`RateLimits:Account`).

There is no staff route to export someone else's data. For a request made another way, verify who is asking, then have
them sign in and use the export.

## Deleting an account

A member deletes their own account with `DELETE /me`; staff delete one with `DELETE /admin/users/{externalUserId}`
(logged in the staff audit log). Both return `202 Accepted`.

**At once**, in one transaction: the member's posts and comments are emptied and marked deleted, the text is removed
from moderation events about them, and their profile is anonymised (username `deleted_…`, no display name or avatar,
status `deleted`). Signing in again with the same token makes a new, empty account.

**Within minutes**, the worker, from a `UserDeleted` event: their likes, follows (both ways), favourites, mentions,
reports, photos and their files, feed log, and everything in Redis about them (their timeline, what the ranker learned,
their posts in other members' timelines, and their entry in other members' learned authors). This survives the API
restarting straight after the request, and is safe to run twice.

**What is left**, and why:

| Left | Why | Until |
|---|---|---|
| The account row, anonymised | Other people's comments and reposts still point at it | For good; nothing in it identifies the person |
| Their posts and comments as empty "deleted" placeholders | Replies to them still read in order | For good, empty |
| Other members' comments and reposts on their posts | They belong to those members | Those members delete them |
| Moderation events about their content, without the text | Records of what happened (a block, a suspension) | `Retention:ModerationEvents` |
| Copies of their events in the outbox and Kafka | | `Retention:PublishedOutbox`, `Kafka:Retention` (7 days); dead-letter topics 28 days |
| Logs that mention their user ID | | Your log platform's retention |
| Backups | | Your backup retention |

## Still to do by people

- A DPIA (data protection impact assessment) covering the above, before real members join.
- A privacy notice that states these periods.
- Set the log and backup retention in your platform to match this page.
