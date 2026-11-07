# Production readiness

What FeedFabric needs before it runs for real members, and where each item stands. The domain was already in good
shape (outbox to Kafka, idempotent consumers, OIDC auth with role policies, a safety pipeline that blocks when a check
can't run, signed upload URLs); this plan covered the layer around it that lets it be deployed, operated and trusted.

**Done means:** the API and worker deploy to a real environment from GitHub Actions, survive restarts and bad input,
can be observed and operated, and meet UK GDPR obligations for health-adjacent user content.

## Still open

| Item | What's left | Owner |
|---|---|---|
| 2 | Branch protection on `main`, requiring the CI checks. Repository settings, not code. | Kieran |
| 12 | A DPIA and a privacy notice ([data-retention.md](data-retention.md#still-to-do-by-people)) | Kieran |
| 16 | Move `EmbeddingIndex` out of each API instance (needs a pgvector migration, so not done with this plan) | |
| 16 | Set the load target, then run `tools/load/feeds.js` against it | Kieran |
| 18 | The staff audit log is structured logs; move it to a table if staff need to search it in the admin panel (needs a migration) | |

## P0: blockers

### 1. Land the work in progress
**Done.** The work is on `main`, and the migrations are squashed into `InitialCreate`.

### 2. CI on GitHub Actions
**Done**, apart from branch protection. `.github/workflows/ci.yml` builds and tests the solution, with the models
cached by `ml/fetch_models.py`'s hash so the model tests run. It also builds `web/admin` and `web/fabric-ui`, then
builds, scans and publishes the three images.

### 3. Remove template leftovers
**Done.** `/weatherforecast` is gone from the API and from `FeedFabric.Api.http`.

### 4. Input validation
**Done.** Posts are capped at 5,000 characters and comments at 2,000 (`too_long`). Request bodies are capped at
1 MB (`Kestrel:Limits:MaxRequestBodySize`, `413 request_too_large`). Both are listed in [errors.md](errors.md).

### 5. Rate limiting
**Done.** Fixed-window limits per user (per IP when signed out) on posting, commenting, reactions, reports, uploads,
signals, ranking previews, and the account export and deletion. Over a limit is `429 rate_limited` with
`Retry-After`. The limits are set under `RateLimits` ([configuration.md](configuration.md#rate-limits-api)). They are
counted per instance ([deploying.md](deploying.md#scaling)).

### 6. Make the worker's failures safe
**Done.**
- A handler failure is tried `Kafka:MaxHandlerAttempts` times (5), then the event goes to `<topic>.dead-letter` with
  its error, and the partition moves on. A failure because a dependency is down is retried until it's back.
- The outbox parks a message after `Outbox:MaxPublishAttempts` (10). Parked messages are counted in the admin panel.
- Order: later events for the same key are still handled. That is safe because handlers are idempotent and check the
  current state. The reasoning is in `EventConsumer`, and the replay steps are in
  [runbooks.md](runbooks.md#a-consumer-dead-lettered-events).

### 7. Health checks
**Done.** `/health/live` and `/health/ready` on both. Each image has a `HEALTHCHECK` that calls the app itself,
because the images have no curl. The checks are in [deploying.md](deploying.md#health-checks).

### 8. Migrations as a deploy step
**Done.** The `migrations` image is an EF migration bundle that runs before the API rolls out
([deploying.md](deploying.md#order)).

### 9. Production configuration and secrets
**Done.**
- Startup fails on a missing connection string or `Auth:Jwt` setting.
- Startup also fails on `Auth:Mode=Dev` or local blob storage outside Development.
- `ForwardedHeaders`, HSTS and a CORS policy (`Cors:AllowedOrigins`) are in.
- Every setting is in [configuration.md](configuration.md).

## P1: operability

### 10. Observability
**Done.**
- OpenTelemetry covers ASP.NET Core, Postgres (Npgsql), Redis, HTTP and the worker's own spans. Trace context travels in Kafka
  headers, so one request can be followed into the worker. Data goes out over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT`
  is set.
- Logs are JSON on the console.
- Metrics: `feedfabric.outbox.waiting`, `.lag` and `.parked`; `feedfabric.consumer.lag`;
  `feedfabric.events.handled` and `.dead_lettered`; `feedfabric.moderation.checks`; `feedfabric.ranking.rankings`
  (with the fallback rate). Feed latency comes from ASP.NET Core's request duration, by route.

### 11. Data retention
**Done.**
- The worker's retention job deletes published outbox messages, processed-event records, the feed log, decided
  moderation events, abandoned and rejected media, and old Redis post totals. Each one runs on its own period under
  `Retention:*`.
- Per-post Redis keys now expire. Older keys are given an expiry once a day.
- [data-retention.md](data-retention.md) is the written policy.

### 12. GDPR: deletion and export
**Done**, apart from the DPIA.
- `DELETE /me` and `DELETE /admin/users/{externalUserId}` take posts, comments and the profile down at once.
- The worker then removes the rest: Postgres rows, Redis (including other members' timelines), embeddings, blobs and
  the feed log.
- `GET /me/export` is the subject access export.
- What deletion leaves behind, and why, is in [data-retention.md](data-retention.md#deleting-an-account). No schema
  change was needed: the account row stays as an anonymised tombstone.

### 13. Resilience
**Done.**
- S3 has a timeout and standard retries.
- ClamAV retries when it can't connect, then waits on Kafka. Nothing is published unscanned.
- When Postgres, Redis or S3 can't be reached, the API answers `503 service_unavailable` with `Retry-After`.
- The home feed falls back to Postgres. A lost or cleared timeline also continues from Postgres.
- The worker's Redis connection no longer aborts on connect failure.
- `ShutdownTimeout` (25 s) applies to both services.
- What each path does with Redis down is in [runbooks.md](runbooks.md#redis-is-down), and it's tested.

### 14. Models in production
**Done.**
- Every model file's SHA-256 is in its `meta.json` (the ranker's in `latest.json`), and a mismatch is refused.
- The readiness checks wait on the models.
- A ranker trained on simulated data is refused outside Development (`Ranking:AllowSimulatedRanker`).
- How to swap a model is in [runbooks.md](runbooks.md#swap-a-model).

### 15. Integration tests
**Done.**
- `tests/FeedFabric.Worker.Tests/PipelineTests.cs` runs the real worker against Postgres, Redis and Kafka in
  Testcontainers. It covers fan-out to the home feed, a rebuilt timeline, likes and comments reaching the signals,
  report → hold → approve or remove, and upload → scan → ready.
- `tests/FeedFabric.Api.Tests/AuthorizationTests.cs` checks each policy and that every staff route has one. It also
  covers suspended users and `Auth:Mode=Dev` refusing to start outside Development.

## P2: scale and hardening

### 16. Scaling limits
**Partly done.**
- `/admin/dashboard` is cached per window for 15 seconds, and only one instance builds it at a time.
- `tools/load/feeds.js` is a k6 load test for the home and For You feeds. It fails the run when p95 is over the
  target.
- Open: the target itself, and `EmbeddingIndex`. The index is still in memory in each API instance and only holds a
  day's posts. pgvector would need a migration.

### 17. Kafka in production
**Done.**
- Topics the worker creates have `Kafka:ReplicationFactor` (3), `Kafka:Partitions` (3) and retention settings: 7
  days for events, 28 days for dead-letter topics.
- `Kafka:Security` configures SASL/TLS for managed Kafka.

### 18. Security
**Done**, apart from the open rows above.
- API responses carry security headers: `nosniff`, `X-Frame-Options: DENY`, a deny-all CSP, `Referrer-Policy`,
  `Permissions-Policy`, and `no-store` by default. They don't carry a `Server` header.
- Staff changes are logged in the `FeedFabric.Audit` category ([runbooks.md](runbooks.md#the-staff-audit-log)).
- Dependabot covers NuGet, npm, uv, Docker and Actions. The Actions are pinned to commit SHAs.
- Trivy scans each image in CI and fails on fixable critical or high vulnerabilities. The images install Ubuntu's
  security updates.
- Security review of `Auth` and the admin controllers:
  - Fixed: a token could carry FeedFabric's own `ff:` claims and grant itself roles. They're now stripped.
  - Confirmed: every staff route has a staff policy (there's a test for this), and `/admin/system` shows no secrets.

### 19. Runbooks
**Done.** [runbooks.md](runbooks.md) covers deploy, rollback, the dead-letter topics, parked outbox messages,
rebuilding Redis timelines, Redis down, swapping a model, the moderation backlog, erasing an account and the audit
log. [deploying.md](deploying.md) is the deployment guide.

## Decisions (owner: Kieran)
- **Deployment target:** Docker, from the images CI publishes to GHCR.
- **Load target** for item 16: to be set (requests per second and p95).
