# Runbooks

What to do when something needs a person. Deploying is in [deploying.md](deploying.md); settings are in
[configuration.md](configuration.md).

Every staff route below needs a bearer token with a staff role (`Auth:Roles`), and every change through one is logged
in the `FeedFabric.Audit` log category with who made it.

## Deploy

1. Wait for CI to pass on `main`. It publishes `ghcr.io/<owner>/<repo>/api`, `worker-events` and `migrations`, each
   tagged `latest` and `sha-<short>`, and `1.2.3` and `1.2` for a `v1.2.3` tag. Deploy by `sha-` or version tag, never
   `latest`, so a rollback has something to go back to.
2. Run the migrations image with that tag ([deploying.md](deploying.md#migrations)). A failure exits non-zero and
   leaves the database as it was: stop here.
3. Roll out the worker, then the API. Each instance must pass `/health/ready` before the next is replaced.
4. Check the admin panel's *Settings and models* page: the event pipeline has no growing backlog, the models are
   present, and the ranker is the one you expect.

## Roll back

1. Redeploy the previous image tag of the API and the worker, in the same order.
2. Leave the database alone if the migrations only added things (new tables, nullable columns, indexes): the old code
   ignores them. Write migrations that way, and drop things a release later.
3. If a migration must be undone, run the previous bundle to the migration before it, with the old image's tag:
   `docker run --rm --entrypoint ./efbundle -e … ghcr.io/…/migrations:<old tag> <migration name> --connection "…"`.
   This runs the migration's `Down`, which can lose data: take a database snapshot first.

## A consumer dead-lettered events

The `feedfabric.events.dead_lettered` metric counts them; alert on anything above zero. An event goes to its
topic's dead-letter topic (`feedfabric.posts.dead-letter`, `feedfabric.engagement.dead-letter`,
`feedfabric.media.dead-letter`) after `Kafka:MaxHandlerAttempts` failures that weren't a dependency being down. They
are kept for 28 days (`Kafka:DeadLetterRetention`).

1. Read them. Each carries `dead-letter.group`, `dead-letter.attempts` and `dead-letter.error` headers, and the
   original key, `type` header and payload:
   ```sh
   kcat -C -b "$BROKERS" -t feedfabric.posts.dead-letter -o beginning -e \
     -f 'key=%k partition=%p offset=%o\n%h\n%s\n\n'
   ```
   The worker's logs have the full exception, under the same error.
2. Fix the cause and deploy the fix. Don't replay before that: the event will just come back.
3. Check the event still makes sense against Postgres, which is the source of truth. Events for the same key after the
   dead-lettered one were handled, so a replay can arrive out of order: replaying an old "post edited" after the post
   was deleted is harmless (handlers check the current state), but read the payload first.
4. Replay it onto the original topic with its key and `type` header:
   ```sh
   printf '%s' "$PAYLOAD" | kcat -P -b "$BROKERS" -t feedfabric.posts -k "$KEY" -H "type=$TYPE"
   ```
   Every consumer group on that topic receives it again. The groups that already handled it skip it, because each
   records the event's ID (`ProcessedEvents`, kept for 35 days, longer than the dead-letter topics keep events).

Add the `Kafka:Security` settings to kcat (`-X security.protocol=SASL_SSL -X sasl.mechanism=SCRAM-SHA-512 -X
sasl.username=… -X sasl.password=…`) for managed Kafka.

## Parked outbox messages

The outbox publisher gives up on a message after `Outbox:MaxPublishAttempts` (10) failed publishes, so that one bad
message can't hold up the others. The *Settings and models* page counts parked messages, and each keeps its error:

```sql
SELECT "Id", "Type", "PartitionKey", "Attempts", "LastError", "CreatedAt"
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NULL AND "Attempts" >= 10
ORDER BY "CreatedAt";
```

Usually Kafka was unreachable for longer than the retries lasted, or a topic is missing. Fix that, then release them
and the publisher sends them on its next poll:

```sql
UPDATE "OutboxMessages" SET "Attempts" = 0 WHERE "ProcessedAt" IS NULL AND "Attempts" >= 10;
```

## Rebuild Redis timelines

Home timelines in Redis are a cache of Postgres. When one is missing, the home feed reads the same posts from Postgres;
the next post fanned out starts a new timeline, and pages continue from Postgres once it runs out. So after Redis was
flushed, lost its data, or a timeline looks wrong, there is nothing to rebuild by hand:

- One member: `redis-cli DEL feed:<userId>`.
- Everyone: delete the `feed:*` keys in batches, never with `KEYS` or `FLUSHALL` on a live server:
  ```sh
  redis-cli --scan --pattern 'feed:*' | xargs -n 500 redis-cli DEL
  ```

Expect more Postgres load from the home feed until timelines refill. The ranker's signals (impressions, dwell,
learned authors and topics) are not in Postgres and are not rebuilt: For You ranks by its formula with less to go on
until members' activity builds them up again.

## Redis is down

- **API:** readiness reports Redis as degraded but stays ready, so instances aren't pulled. The home feed reads
  Postgres. Routes that need Redis (For You, impression and dwell signals, "not interested") return `503`
  `service_unavailable` with `Retry-After: 5`.
- **Worker:** readiness fails. Consumers retry the events that need Redis until it's back, without dead-lettering
  them, so nothing is lost; Kafka consumer lag grows meanwhile and drains after.
- **After:** check consumer lag returns to zero. Timelines missed posts fanned out during the outage only if Redis
  lost its data; see [Rebuild Redis timelines](#rebuild-redis-timelines).

## Swap a model

The safety and embedding models live in blob storage under `models/`, each folder with a `meta.json` that holds the
SHA-256 of every file. A file that doesn't match is refused: posts and uploads wait (readiness fails) rather than run
unchecked.

**Toxicity, nudity or embeddings:**

1. Change the pinned revision in `ml/fetch_models.py`, or retrain the toxicity model with `make tune-toxicity`, which
   only publishes if it passes its tests.
2. Publish: `uv run ml/fetch_models.py --s3-bucket <bucket>`.
3. Restart the API and the worker. They load these models once per process, so they keep the old one until then.
4. Check *Settings and models* shows the models present, and watch the moderation overview's block and hold rates for
   a day. If the new model blocks too much, publish the old revision and restart again.

**Ranker:** `uv run ml/train_ranker.py --s3-bucket <bucket>` trains on the real feed log, writes
`models/ranker/<version>.onnx` and points `models/ranker/latest.json` at it. The API checks for a new version every
minute; no restart. To go back, copy the previous `latest.json` over the current one (keep a copy before each training
run). A ranker trained with `--source synthetic` or `all` is refused outside Development
(`Ranking:AllowSimulatedRanker`).

## Work down a moderation backlog

The admin panel's *Review queue* (`GET /admin/moderation/queue`) lists posts and comments waiting for a decision,
oldest first: those the toxicity model held or flagged, and those with `Moderation:ReportsToHold` (3) reports. Held
content is seen only by its author until someone decides. *Dashboard* and `GET /admin/moderation/overview` show how
many are waiting, the oldest, and the last day's blocks, holds, flags and reports.

1. Decide the oldest first: `POST /admin/moderation/posts/{id}/decision` or `/comments/{id}/decision` with
   `{ "decision": "approved" }` or `{ "decision": "removed" }`.
2. If holds jump, check the *Moderation log* for which label is behind them and look at a sample. Posts that
   should have been fine mean the threshold is too low: raise `Moderation:ToxicityHoldAt` for that label, or fine-tune
   the model (`make tune-toxicity`) for the wording it gets wrong.
3. If a wave of reports comes from a few accounts, check those accounts before acting on what they reported.
   Suspending an account (*Users*, or `PUT /admin/users/{externalUserId}/status`) stops it posting and reporting.

## Erase an account

For a request that came by email or support rather than `DELETE /me`:

1. Verify the person is the account holder.
2. `DELETE /admin/users/{externalUserId}` returns `202`. Their posts and profile go at once; the worker removes the
   rest within minutes ([data-retention.md](data-retention.md#deleting-an-account) lists what's left and why).
3. Check `GET /admin/users/{externalUserId}` shows status `deleted`, and that the worker logged the erasure without
   dead-lettering a `UserDeleted` event. If it was dead-lettered, fix the cause and replay it (above); erasure is safe
   to run twice.

## The staff audit log

Every change through `/admin` or `/ranking` (suspending, verifying or deleting a user, a moderation decision, saving
or deleting a ranking profile, setting the default, saving topics) is logged in the `FeedFabric.Audit` category:

```
Staff action POST /admin/moderation/posts/…/decision by <externalUserId> (roles moderator) from 203.0.113.7: 200
```

with the method, path, actor, roles, address and status as structured fields. Route that category to log storage staff
can't edit or delete, and keep it as long as the moderation events it explains (`Retention:ModerationEvents`, a year).
To see who did what, search it by actor or by the ID in the path. The moderation log (`GET /admin/moderation/log`)
and ranking profile and topic versions also record who decided what.
