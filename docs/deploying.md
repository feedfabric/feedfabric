# Deploying

FeedFabric runs as three Docker images that CI builds, scans and publishes to GitHub Container Registry. This page is
what a deployment needs from your platform. The steps for each release, and for rolling one back, are in
[runbooks.md](runbooks.md#deploy).

## Images

| Image | Built from | Runs |
|---|---|---|
| `ghcr.io/<owner>/<repo>/api` | `src/FeedFabric.Api/Dockerfile` | The API, on port 8080 |
| `ghcr.io/<owner>/<repo>/worker-events` | `src/FeedFabric.Worker.Events/Dockerfile` | The worker: outbox publisher, Kafka consumers, retention job. Health on 8080. |
| `ghcr.io/<owner>/<repo>/migrations` | `src/FeedFabric.Application/Dockerfile` | A one-off: applies database migrations, then exits |

Every push to `main` publishes `latest` and `sha-<short>`; a `v1.2.3` tag publishes `1.2.3` and `1.2`. Pull requests
build and scan the images without publishing them. CI fails on a fixable critical or high vulnerability (Trivy), and
the images install Ubuntu's security updates when they're built, so rebuild regularly to pick up new ones.

Images run as a non-root user. They have no shell tools such as curl; each checks its own health (`HEALTHCHECK`).

## Order

1. **Migrations**, to completion, before any new API or worker starts:
   ```sh
   docker run --rm -e ConnectionStrings__Default="Host=…;Database=…;Username=…;Password=…" \
     ghcr.io/<owner>/<repo>/migrations:<tag>
   ```
   It applies the migrations the database doesn't have, each in its own transaction, and exits non-zero on failure.
   Running it again is a no-op. The API and the worker never migrate outside Development.
2. **Worker**, then **API**, one instance at a time, each passing `/health/ready` before the next is replaced.

Migrations have to work with the code before them too, since old instances run until they're replaced: add tables and
nullable columns, and remove things a release later.

## Migrations

The database user the migrations image uses needs to create and alter tables. Give the API and the worker a user that
can only read and write data.

## Health checks

| Path | API | Worker |
|---|---|---|
| `/health/live` | The process is up. Restart the container if it fails. | Same |
| `/health/ready` | Postgres and the toxicity model; Redis only degrades it, since the home feed falls back to Postgres | Postgres, Redis, Kafka, ClamAV (when `MalwareScanning:Enabled`), and the nudity and embedding models |

Send traffic to an API instance only while it's ready. Both paths answer plain HTTP, so probes needn't go through TLS.

## Stopping

On `SIGTERM` the API stops taking requests and finishes those in flight, and the worker finishes the event each
consumer is handling and commits its offset, within `ShutdownTimeout` (25 seconds). Give the container longer than
that before it's killed: `stop_grace_period: 30s` in Compose, `terminationGracePeriodSeconds: 30` in Kubernetes. If it
is killed anyway, nothing is lost: an event whose offset wasn't committed is delivered again, and handlers are
idempotent.

## Scaling

- **API:** stateless apart from caches (the last day's post embeddings, the loaded ranker, users' status), so run as
  many as you need behind a load balancer. Each instance holds the embedding index in memory, which grows with a day's
  posts.
- **Worker:** run several. The outbox publisher locks the rows it sends (`SKIP LOCKED`), each Kafka consumer group
  splits a topic's partitions between instances, and the retention job runs in one instance at a time (a Postgres
  advisory lock). More instances than a topic's partitions (`Kafka:Partitions`, 3) leaves the extra ones idle for that
  topic.
- **Rate limits** are counted per instance, so with N API instances a user can make up to N times each limit. Keep
  that in mind when setting `RateLimits`, or limit per user in front of the API.

## What the platform provides

| Dependency | Notes |
|---|---|
| PostgreSQL 17 | `ConnectionStrings:Default` |
| Valkey 8 or Redis 7 | `ConnectionStrings:Redis`. Valkey is the BSD-licensed fork; anything speaking the Redis protocol works. Persistence is optional: a lost timeline is rebuilt by fan-out ([runbooks.md](runbooks.md#rebuild-redis-timelines)). |
| Kafka | `Kafka:BootstrapServers`, with 3 or more brokers (`Kafka:ReplicationFactor`) and `Kafka:Security` for SASL/TLS. The worker creates its topics on startup. |
| S3 or S3-compatible storage | `BlobStorage:S3:*`, with the models published to it (`ml/fetch_models.py --s3-bucket`) |
| ClamAV (clamd) | `MalwareScanning:ClamAv:Host`, reachable from the worker |
| An OpenID Connect identity provider | `Auth:Jwt:*` ([auth.md](auth.md)) |
| A reverse proxy or load balancer terminating TLS | `ForwardedHeaders:*` |
| An OpenTelemetry collector (optional) | `OTEL_EXPORTER_OTLP_ENDPOINT` |

Every setting and its environment variable is in [configuration.md](configuration.md), with a production checklist.
Pass secrets in from your platform's secret store.

## The admin panel

`make admin-build` writes a static site to `web/admin/dist`. Serve it from the API's origin with `/admin`, `/ranking`,
`/blobs`, `/userfeed` and `/media` forwarded to the API, or from its own origin with that origin in
`Cors:AllowedOrigins`.
