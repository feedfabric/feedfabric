---
layout: ../../layouts/Doc.astro
title: Deploying FeedFabric
description: Put FeedFabric on a server for real members. What runs where, the images, a single-server Docker Compose setup behind Caddy, the settings it needs, migrations, models, the admin panel, and upgrades.
---

This page takes FeedFabric from `make up` on a laptop to a server real members use. It describes one complete,
working setup (Docker Compose on a single Linux host behind Caddy) and what to change for anything larger.
[Operating it](/operating/) has the settings reference and what's still to do before a large launch, and
[Cloudflare](/deploy/cloudflare/) covers R2 for storage and hosting the static parts on Cloudflare.

<div class="warn">

**Before real members join**, read `docs/production-readiness.md` for what's still open (a DPIA and privacy notice,
a load target) and `docs/data-retention.md` for what FeedFabric keeps and for how long. `docs/runbooks.md` has what
to do when something needs a person: dead-lettered events, rollbacks, swapping a model, erasing an account.

</div>

## What you're deploying

| Part | What it is | Needs |
|---|---|---|
| **API** | `ghcr.io/feedfabric/feedfabric/api`, listening on 8080 | Postgres, Valkey, blob storage, your identity provider |
| **Worker** | `ghcr.io/feedfabric/feedfabric/worker-events` | Postgres, Valkey, Kafka, blob storage, ClamAV (unless turned off) |
| **Migrations** | `ghcr.io/feedfabric/feedfabric/migrations`, a one-off run before each deploy | Postgres |
| **PostgreSQL 17** | The source of truth | A volume and backups |
| **Valkey 8** (or Redis) | Timelines, sessions, counters, embeddings: all rebuildable | Append-only persistence is enough |
| **Kafka 4** | Events from the outbox to the worker's consumers | One broker is fine to start; the outbox in Postgres means a lost broker loses nothing |
| **Blob storage** | Photos and the models | Any S3-compatible service: S3, R2, MinIO, Spaces |
| **ClamAV** | Malware scanning for uploads | About 3 GB of memory; or `MalwareScanning:Enabled=false` |
| **Identity provider** | Signs members in; FeedFabric checks its tokens | Auth0, Entra ID, Cognito, Keycloak or any OpenID Connect server ([Sign-in and users](/auth/)) |
| **Admin panel** | `web/admin`, a static site | Same origin as the API, or a proxy to it |
| **Your front end** | Static or server-rendered | Same origin as the API, or its origin in `Cors__AllowedOrigins__0` |

The simulator is for demos and never runs in production. Neither does `Auth:Mode` `Dev`: it refuses to start outside
Development.

## Sizing a first server

For a community in the thousands, one host runs everything: **4 vCPUs and 8 GB of memory** (16 GB with ClamAV) and
fast disk for Postgres. The API keeps the last day's post embeddings and the loaded models in memory, and ranks on
CPU, so CPU matters more than anything else as For You traffic grows. To grow past one host, move Postgres, Valkey
and Kafka to managed services and run more API instances; see [Scaling](#scaling).

## 1. Images

CI (`.github/workflows/ci.yml`) scans the three images with Trivy and publishes them to GitHub Container Registry on
every push to `main` and every version tag:

| Tag | When |
|---|---|
| `latest` | Every push to `main` |
| `sha-<short>` | Every push to `main` or a tag |
| `1.4.2` and `1.4` | A tag `v1.4.2` |

**Pin a version or a `sha-` tag in production**, never `latest`, so a restart can't upgrade you by surprise. To build
the images yourself, from the repository root:

```bash
docker build -f src/FeedFabric.Api/Dockerfile -t feedfabric-api .
docker build -f src/FeedFabric.Worker.Events/Dockerfile -t feedfabric-worker .
docker build -f src/FeedFabric.Application/Dockerfile -t feedfabric-migrations .
```

## 2. Blob storage

Create a private bucket, an access key that can read and write it, and a CORS rule so browsers can upload photos
straight to it with the signed URLs the API hands out:

```json
[
  {
    "AllowedOrigins": ["https://social.example.org"],
    "AllowedMethods": ["PUT", "GET"],
    "AllowedHeaders": ["Content-Type"],
    "MaxAgeSeconds": 3600
  }
]
```

Members' photos are only ever read through signed URLs that expire (an hour by default), so the bucket never needs
to be public. On Cloudflare, use R2: [Cloudflare](/deploy/cloudflare/#r2-for-photos-and-models) has the settings.

## 3. The models

Text and photos aren't published without the safety models: a post whose checks can't run is blocked. Put them in
the bucket before the first member posts. From a checkout, with the bucket's credentials in the usual AWS variables:

```bash
cd ml
export AWS_ACCESS_KEY_ID=… AWS_SECRET_ACCESS_KEY=…
uv run fetch_models.py --s3-bucket feedfabric-prod                         # nudity, offensive language, MiniLM
uv run --group tune tune_toxicity.py --s3-bucket feedfabric-prod           # recommended: tuned to this app's wording
# For an S3-compatible service, add --s3-endpoint https://…
```

`tune_toxicity.py` takes about 45 minutes and won't publish a model that fails its tests. Without it, the stock
offensive-language model is used, which flags more friendly banter.

**The ranker comes later.** For You uses the built-in formula until a model is trained, which is fine on day one.
Once real members have a few weeks of reading history, train on them and publish to the same bucket; the API picks
it up within a minute:

```bash
DATABASE_URL=postgresql://… uv run train_ranker.py --source real --s3-bucket feedfabric-prod
```

Never publish a ranker trained on simulated data to a production bucket. See
[Feed log and training](/how-it-works/training/).

## 4. Configuration

Settings are environment variables, `:` written as `__`. A production `.env` for the Compose file below, with every
secret in it. Keep it out of the repository, readable only by root (`chmod 600`), or use your platform's secret
store instead.

```bash
# /opt/feedfabric/.env
FEEDFABRIC_VERSION=1.4.2
DOMAIN=social.example.org
STAFF_DOMAIN=staff.social.example.org

POSTGRES_DB=feedfabric
POSTGRES_USER=feedfabric
POSTGRES_PASSWORD=<long random>

# Your identity provider: see /auth/ for Auth0, Entra ID, Cognito and Keycloak values
AUTH_AUTHORITY=https://login.example.org/
AUTH_AUDIENCE=https://social.example.org/api

S3_BUCKET=feedfabric-prod
S3_REGION=auto
S3_SERVICE_URL=https://<account-id>.r2.cloudflarestorage.com   # empty for AWS
S3_ACCESS_KEY=<key id>
S3_SECRET_KEY=<secret>
```

The minimum each service needs, beyond the connection strings:

| Setting | API | Worker | Notes |
|---|:-:|:-:|---|
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` = `Production` | ✓ | ✓ | Anything but `Development` is production |
| `Auth__Jwt__Authority`, `Auth__Jwt__Audience` | ✓ | | Or `SigningKey` and `Issuer` for tokens you sign yourself |
| `Auth__Roles__Moderator__0`, `Auth__Roles__RankingAdmin__0` | ✓ | | Your identity provider's role names for staff |
| `BlobStorage__Provider` = `S3`, `BlobStorage__S3__*` | ✓ | ✓ | Both must point at the same bucket |
| `Kafka__BootstrapServers` | | ✓ | |
| `MalwareScanning__ClamAv__Host` | | ✓ | Or `MalwareScanning__Enabled=false` on both |
| `Moderation__BlockedTerms__0`… | ✓ | ✓ | Your real list, kept out of the repository |
| `AllowedHosts` | ✓ | | Your domain |
| `ForwardedHeaders__Enabled` = `true` | ✓ | | Behind Caddy, so the API sees members' real addresses and HTTPS |
| `Kafka__ReplicationFactor` = `1` | | ✓ | With a single broker; the default, 3, needs three |

Every setting is in `docs/configuration.md`; [Operating it](/operating/#which-service-reads-what) lists which
service reads which, and the ones that must match.

## 5. Docker Compose

One file for everything on the host. Caddy gets certificates, serves your front end on `DOMAIN` and the admin panel
on `STAFF_DOMAIN`, and sends the API's routes on each to the API, so each app sees the API on its own origin.

```yaml
# /opt/feedfabric/compose.yml
x-app: &app
  restart: unless-stopped
  # Longer than the services' ShutdownTimeout (25 seconds), so in-flight requests and events finish on a restart
  stop_grace_period: 30s
  depends_on:
    db: { condition: service_healthy }
    redis: { condition: service_healthy }
    kafka: { condition: service_healthy }

x-storage: &storage
  BlobStorage__Provider: S3
  BlobStorage__S3__Bucket: ${S3_BUCKET}
  BlobStorage__S3__Region: ${S3_REGION}
  BlobStorage__S3__ServiceUrl: ${S3_SERVICE_URL}
  BlobStorage__S3__AccessKey: ${S3_ACCESS_KEY}
  BlobStorage__S3__SecretKey: ${S3_SECRET_KEY}
  BlobStorage__S3__ForcePathStyle: "true"
  ConnectionStrings__Default: Host=db;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
  ConnectionStrings__Redis: redis:6379

services:
  api:
    <<: *app
    image: ghcr.io/feedfabric/feedfabric/api:${FEEDFABRIC_VERSION}
    environment:
      <<: *storage
      ASPNETCORE_ENVIRONMENT: Production
      AllowedHosts: ${DOMAIN};${STAFF_DOMAIN}
      # Only Caddy can reach the API on this network, so its X-Forwarded-* headers can be trusted
      ForwardedHeaders__Enabled: "true"
      Auth__Jwt__Authority: ${AUTH_AUTHORITY}
      Auth__Jwt__Audience: ${AUTH_AUDIENCE}

  worker:
    <<: *app
    image: ghcr.io/feedfabric/feedfabric/worker-events:${FEEDFABRIC_VERSION}
    environment:
      <<: *storage
      DOTNET_ENVIRONMENT: Production
      Kafka__BootstrapServers: kafka:29092
      # One broker, so one copy of each partition
      Kafka__ReplicationFactor: "1"
      MalwareScanning__ClamAv__Host: clamav

  # Not started with the others: `docker compose run --rm migrate` before each deploy
  migrate:
    image: ghcr.io/feedfabric/feedfabric/migrations:${FEEDFABRIC_VERSION}
    profiles: [deploy]
    depends_on:
      db: { condition: service_healthy }
    environment:
      ConnectionStrings__Default: Host=db;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}

  db:
    image: postgres:17
    restart: always
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      retries: 10

  # Valkey: the BSD-licensed fork of Redis. Redis itself works too.
  redis:
    image: valkey/valkey:8
    restart: always
    command: ["valkey-server", "--appendonly", "yes"]
    volumes: [redisdata:/data]
    healthcheck:
      test: ["CMD", "valkey-cli", "ping"]
      interval: 5s
      retries: 10

  # One broker in KRaft mode, reachable only inside this network
  kafka:
    image: apache/kafka:4.0.0
    restart: always
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller
      KAFKA_LISTENERS: INTERNAL://:29092,CONTROLLER://:9093
      KAFKA_ADVERTISED_LISTENERS: INTERNAL://kafka:29092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: INTERNAL
      KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
      KAFKA_CONTROLLER_QUORUM_VOTERS: 1@localhost:9093
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
      # On the volume, so events published but not yet consumed survive a restart
      KAFKA_LOG_DIRS: /var/lib/kafka/data
    volumes: [kafkadata:/var/lib/kafka/data]
    healthcheck:
      test: ["CMD-SHELL", "/opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:29092 > /dev/null 2>&1"]
      interval: 10s
      retries: 20
      start_period: 15s

  clamav:
    image: clamav/clamav-debian:1.4
    restart: always
    volumes: [clamavdb:/var/lib/clamav]

  caddy:
    image: caddy:2
    restart: always
    ports: ["80:80", "443:443", "443:443/udp"]
    environment:
      DOMAIN: ${DOMAIN}
      STAFF_DOMAIN: ${STAFF_DOMAIN}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - ./site:/srv/site:ro          # your front end's build
      - ./admin:/srv/admin:ro        # web/admin/dist
      - caddydata:/data

volumes: { pgdata: {}, redisdata: {}, kafkadata: {}, clamavdb: {}, caddydata: {} }
```

Only Caddy publishes ports; Postgres, Valkey, Kafka and the API are reachable only from inside the Compose network.

### The reverse proxy

```text
# /opt/feedfabric/Caddyfile
{$DOMAIN} {
	encode zstd gzip

	# The API's routes. Anything not listed here never reaches it.
	@api path /userfeed* /follows* /favourites* /media* /me /me/* /ranking* /admin/*
	handle @api {
		reverse_proxy api:8080
	}

	# Your front end, a single-page app
	handle {
		root * /srv/site
		try_files {path} /index.html
		file_server
	}

	import security
}

# The admin panel, at the root of its own host name: its routes (/users, /review…) live at the root
{$STAFF_DOMAIN} {
	encode zstd gzip

	@api path /userfeed* /follows* /favourites* /media* /me /me/* /ranking* /admin/*
	handle @api {
		reverse_proxy api:8080
	}

	handle {
		root * /srv/admin
		try_files {path} /index.html
		file_server
	}

	import security
}
```

Put the shared headers at the top of the same file. The API sets its own security headers on its responses; these
cover the static sites:

```text
(security) {
	header {
		Strict-Transport-Security "max-age=31536000; includeSubDomains"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
}
```

Build the admin panel with `make admin-build` (or `cd web/admin && npm ci && npm run build`) and copy
`web/admin/dist/` to `/opt/feedfabric/admin`. Staff sign in by pasting a bearer token from your identity provider,
carrying a moderator or ranking-admin role, into the panel; it keeps the token in that browser only.
[Cloudflare](/deploy/cloudflare/#the-admin-panel-and-your-front-end) shows how to host it on Cloudflare instead,
behind Cloudflare Access.

<div class="note">

**Rate limits:** the API limits each member's posts, comments, reactions, reports, uploads and signals itself
(`RateLimits`, in `docs/configuration.md`), answering `429` with `Retry-After`. A per-IP limit in the proxy (Caddy's
`rate_limit` plugin, nginx's `limit_req`, Cloudflare's rules) is still worth having against floods from signed-out
clients.

</div>

## 6. Migrations

Only Development migrates the database on startup. In production, migrate as a step of every deploy, **before** the
new API and worker start. The `migrations` image is EF Core's migration bundle for that version: it applies only the
migrations the database doesn't have yet, each in its own transaction, and exits non-zero if one fails. With the
Compose file above:

```bash
docker compose run --rm migrate
```

If you'd rather review SQL first, `dotnet ef migrations script --idempotent` writes the same changes as a script.

## 7. First start

```bash
cd /opt/feedfabric
docker compose pull
docker compose up -d db redis kafka clamav
docker compose run --rm migrate
docker compose up -d
docker compose logs -f api worker
```

ClamAV downloads its virus database on first start, which takes a few minutes; until it's ready, uploads wait in
quarantine. The worker creates the Kafka topics it needs on startup.

Then check it end to end:

1. Sign in with your identity provider and call `GET /me` with the token: you get your `externalUserId`, and
   FeedFabric has created your user.
2. Post something through your front end (or the admin panel's composer) and see it in `GET /userfeed/home`.
3. Upload a photo: it should go from `processing` to `ready` within seconds once ClamAV is up.
4. Check `docker compose ps`: the API and the worker report `healthy` once their `/health/ready` checks pass.
5. Open the admin panel's *Settings and models*: it shows the sign-in setup, which models loaded, and the event
   pipeline's lag. A growing outbox means the worker can't reach Kafka.

## Upgrading

```bash
cd /opt/feedfabric
sed -i 's/^FEEDFABRIC_VERSION=.*/FEEDFABRIC_VERSION=1.5.0/' .env
docker compose pull migrate api worker
docker compose run --rm migrate
docker compose up -d worker
docker compose up -d api
```

Look at what changed under `src/FeedFabric.Application/Migrations` between the two versions before upgrading. Most
migrations add things the old version ignores, so the old API keeps running while you migrate; for one that renames
or drops something, stop the API and worker first. Valkey needs nothing; anything in it can be rebuilt.

To roll back, set the previous version and start it again. Migrations aren't rolled back automatically; roll back
the schema yourself only if the new one stops the old version working, as `docs/runbooks.md` describes.

## Backups

- **Postgres:** everything that matters. Nightly `pg_dump` (or your provider's point-in-time recovery), stored off
  the host, and a restore you've actually tried.
- **The bucket:** members' photos and your trained models. Turn on versioning or replicate it.
- **Valkey and Kafka:** no backups needed. Timelines and counters can be rebuilt from Postgres, and unpublished
  events wait in the outbox.

## Scaling

- **API:** stateless apart from caches, so run as many instances as you like behind a load balancer. Each keeps
  the last day's post embeddings and the models in memory.
- **Worker:** several can run side by side. The outbox publisher uses `SKIP LOCKED`, and each consumer group shares
  its topic's partitions, so `Kafka:Partitions` (3 by default) is the most instances that help. The retention job
  runs in one of them at a time.
- **Rate limits** are counted per API instance, so with three instances a member can make three times each limit.
- **Postgres:** the first thing to move to a managed service. Add a connection pooler (PgBouncer) once you run more
  than a few API instances.
- **Kafka:** for a cluster, leave `Kafka:ReplicationFactor` at 3 and set `Kafka:Security` for SASL/TLS. The worker
  creates its topics with those settings and `Kafka:Partitions`; create them yourself first to size them differently.
- **Kubernetes or a container platform** (ECS, Cloud Run, Fly.io, Azure Container Apps): the same images and
  the same environment variables, with the migrations image as a job that runs before each rollout, and
  `terminationGracePeriodSeconds: 30`. A worker must
  run continuously, so not on a platform that scales to zero.
