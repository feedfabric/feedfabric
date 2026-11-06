# Production readiness

What FeedFabric needs before it runs for real members. The domain is in good shape (outbox to Kafka, idempotent
consumers, OIDC auth with role policies, a safety pipeline that blocks when a check can't run, signed upload URLs);
what's missing is the layer around it that lets it be deployed, operated and trusted.

**Done means:** the API and worker deploy to a real environment from GitHub Actions, survive restarts and bad input,
can be observed and operated, and meet UK GDPR obligations for health-adjacent user content.

Every item ends with a green CI run and, where it applies, a test that proves it. Work P0 first: items 1 to 3 in
order, then 4 to 9 in parallel; then P1; then P2.

## P0: blockers

### 1. Land the work in progress
About 120 files are uncommitted, among them the whole of auth, admin, reports and the review queue, the `web/admin`
panel and two migrations (`AddReviewQueue`, `AddUserDisplayName`). Commit them as reviewable PRs before anything else
builds on them.

### 2. CI on GitHub Actions
**Done:** `.github/workflows/ci.yml`. Still to do by hand: branch protection on `main` requiring its checks.

Workflows that run on pull requests and on `main`:
- `dotnet build` and `dotnet test` for the solution.
- Docker builds of `src/FeedFabric.Api/Dockerfile` and `src/FeedFabric.Worker.Events/Dockerfile`, pushed to GHCR on
  `main` and tags.
- `npm ci && npm run build` for `web/admin`.
- The `TextModeratorTests` (10 of them) fail without the toxicity model (`No vocab.json … run make models`). Either
  fetch the models in CI with `actions/cache` keyed on `ml/fetch_models.py`, or move model tests into a separate job
  or trait so the main job is green without them.
- Branch protection on `main` requiring the checks.

**Done:** a PR shows build, test, images and admin as passing checks.

### 3. Remove template leftovers
`/weatherforecast` and the `WeatherForecast` record in `src/FeedFabric.Api/Program.cs`.

### 4. Input validation
- `PostRequest.Content` and `CommentRequest.Content` have no maximum length. Add caps with an `ErrorCodes` code,
  listed in `docs/errors.md`.
- Kestrel request body size limits.

### 5. Rate limiting
`AddRateLimiter`, partitioned per user, on posting, commenting, likes and reposts, reports, media uploads,
impressions and dwell, and `POST /ranking/preview`. Returns 429 Problem Details with a stable code.

### 6. Make the worker's failures safe
- `Kafka/EventConsumer.cs` retries a failing handler forever (only `JsonException` is skipped), so one poison
  message stalls its partition for that group. Add a maximum attempt count, then publish to a dead-letter topic and
  log or alert.
- `Outbox/OutboxPublisher.cs` also retries failed messages forever. Add a maximum attempt count and a parked state.
- If one message in a batch fails and a later one with the same key succeeds, per-key order breaks. Stop the batch
  for that key, or document why order doesn't matter for those events.

### 7. Health checks
`/health/live` and `/health/ready` on the API and the worker: Postgres, Redis, Kafka, ClamAV (when `MalwareScanning:Enabled`) and models loaded.
Add `HEALTHCHECK` to the Dockerfiles or probes to the deployment.

### 8. Migrations as a deploy step
Startup only migrates in Development. Build an EF migration bundle in CI and run it as a job before the API rolls
out; document the order.

### 9. Production configuration and secrets
- Fail fast at startup when `Auth:Jwt` is empty or a connection string is missing.
- Document every setting as environment variables; secrets come from the platform's secret store, never config files.
- `AllowedHosts`, `ForwardedHeaders` and HSTS behind a proxy; a CORS policy for the admin panel's origin.
- `BlobStorage:Provider` must be `S3` outside Development.

## P1: operability

### 10. Observability
- OpenTelemetry traces and metrics for API, worker, EF Core, Redis and Kafka, with trace context carried in Kafka
  headers so one request can be followed through the worker.
- Structured JSON logs.
- Metrics: outbox lag, consumer lag, dead-letter count, moderation held and blocked rates, ranker fallback rate, p95
  latency of the feed routes.

### 11. Data retention
Nothing is purged today. Add jobs for processed `OutboxMessages` and `ProcessedEvents`, `FeedLog` (one row per post
served), `ModerationEvents`, abandoned media, and the Redis keys that never expire (`impressions:posts`,
`post:{id}:viewers`).

### 12. GDPR: deletion and export
Members write about pregnancy, injuries and sleep, so treat content as likely special-category data.
- Account deletion that cascades through Postgres, Redis timelines and embeddings, blobs and `FeedLog`.
- A subject access export.
- A written retention policy. A DPIA is for people, not agents, but the hooks above are needed for it.

### 13. Resilience
- Retries and timeouts for S3 and ClamAV calls.
- Defined behaviour when Redis is down on every path, not only the home feed. The worker's Redis connection lacks
  `AbortOnConnectFail = false`, unlike the API's.
- Graceful shutdown timeouts for the consumers.

### 14. Models in production
- A versioned, checksummed way to publish the toxicity, nudity and MiniLM models to blob storage, and the readiness
  check from item 7 waiting on them.
- Refuse to serve a ranker trained on simulated data outside Development (today the dashboard only labels it).

### 15. Integration tests
Testcontainers for Postgres, Redis and Kafka, run in CI:
- create post → fan-out → home feed
- like and comment events update counts
- moderation hold → approve or remove
- media upload → scan → ready
- authorization: each policy, suspended users, and `Auth:Mode=Dev` refusing to start outside Development

## P2: scale and hardening

### 16. Scaling limits
- `EmbeddingIndex` is an in-memory brute-force search held in each API instance; move it to pgvector or Redis vector
  search.
- `/admin/dashboard` runs many full-table counts per call; cache or pre-aggregate.
- Load test For You and home feeds against a target (to be set: requests per second and p95).

### 17. Kafka in production
Replication factor of at least 3, SASL/TLS, topic retention and partition counts sized for the load
(`KafkaTopics.EnsureCreatedAsync` creates them today).

### 18. Security
- Security headers on API responses.
- An audit log of admin actions: suspend, verify, moderation decisions, ranking profile saves.
- Dependabot and container image scanning (for example Trivy) in GitHub Actions.
- A security review of `src/FeedFabric.Api/Auth` and the admin controllers.

### 19. Runbooks
In `docs/`: deploy, rollback, replaying the dead-letter topic, rebuilding Redis timelines from Postgres, swapping a
model, working down a moderation backlog.

## Open decisions (owner: Kieran)
- **Deployment target:** decided: Docker, from the images CI publishes to GHCR. Items 7, 8 and 9 target that
  (a `HEALTHCHECK` in each image, a migration bundle run as a one-off container, settings as environment variables).
- **Load target** for item 16.
