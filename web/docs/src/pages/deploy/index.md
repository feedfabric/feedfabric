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

**Read the gaps first.** FeedFabric doesn't yet have health endpoints, rate limiting, CORS, a dead-letter topic for
the worker, or retention jobs; `docs/production-readiness.md` tracks them. It runs well behind a reverse proxy for a
community you know, but put rate limits in the proxy and keep an eye on the worker before opening it to the
internet at large.

</div>

## What you're deploying

| Part | What it is | Needs |
|---|---|---|
| **API** | `ghcr.io/feedfabric/feedfabric/api`, listening on 8080 | Postgres, Redis, blob storage, your identity provider |
| **Worker** | `ghcr.io/feedfabric/feedfabric/worker-events` | Postgres, Redis, Kafka, blob storage, ClamAV (unless turned off) |
| **PostgreSQL 17** | The source of truth | A volume and backups |
| **Redis 7.4** | Timelines, sessions, counters, embeddings: all rebuildable | Append-only persistence is enough |
| **Kafka 4** | Events from the outbox to the worker's consumers | One broker is fine to start; the outbox in Postgres means a lost broker loses nothing |
| **Blob storage** | Photos and the models | Any S3-compatible service: S3, R2, MinIO, Spaces |
| **ClamAV** | Malware scanning for uploads | About 3 GB of memory; or `MalwareScanning:Enabled=false` |
| **Identity provider** | Signs members in; FeedFabric checks its tokens | Auth0, Entra ID, Cognito, Keycloak or any OpenID Connect server ([Sign-in and users](/auth/)) |
| **Admin panel** | `web/admin`, a static site | Same origin as the API, or a proxy to it |
| **Your front end** | Static or server-rendered | Same origin as the API (the API sends no CORS headers) |

The simulator is for demos and never runs in production. Neither does `Auth:Mode` `Dev`: it refuses to start outside
Development.

## Sizing a first server

For a community in the thousands, one host runs everything: **4 vCPUs and 8 GB of memory** (16 GB with ClamAV) and
fast disk for Postgres. The API keeps the last day's post embeddings and the loaded models in memory, and ranks on
CPU, so CPU matters more than anything else as For You traffic grows. To grow past one host, move Postgres, Redis
and Kafka to managed services and run more API instances; see [Scaling](#scaling).

## 1. Images

CI (`.github/workflows/ci.yml`) publishes both images to GitHub Container Registry on every push to `main` and
every version tag:

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

Every setting is in `docs/configuration.md`; [Operating it](/operating/#which-service-reads-what) lists which
service reads which, and the ones that must match.

## 5. Docker Compose

One file for everything on the host. Caddy gets certificates, serves your front end on `DOMAIN` and the admin panel
on `STAFF_DOMAIN`, and sends the API's routes on each to the API, so each app sees the API on its own origin.

```yaml
# /opt/feedfabric/compose.yml
x-app: &app
  restart: unless-stopped
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
      AllowedHosts: ${DOMAIN}
      Auth__Jwt__Authority: ${AUTH_AUTHORITY}
      Auth__Jwt__Audience: ${AUTH_AUDIENCE}

  worker:
    <<: *app
    image: ghcr.io/feedfabric/feedfabric/worker-events:${FEEDFABRIC_VERSION}
    environment:
      <<: *storage
      DOTNET_ENVIRONMENT: Production
      Kafka__BootstrapServers: kafka:29092
      MalwareScanning__ClamAv__Host: clamav

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

  redis:
    image: redis:7.4
    restart: always
    command: ["redis-server", "--appendonly", "yes"]
    volumes: [redisdata:/data]
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
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

Only Caddy publishes ports; Postgres, Redis, Kafka and the API are reachable only from inside the Compose network.

### The reverse proxy

```text
# /opt/feedfabric/Caddyfile
{$DOMAIN} {
	encode zstd gzip

	# The API's routes. Anything not listed here never reaches it.
	@api path /userfeed* /follows* /favourites* /media* /me /ranking* /admin/*
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

	@api path /userfeed* /follows* /favourites* /media* /me /ranking* /admin/*
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

Put the shared headers at the top of the same file:

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

**Rate limits belong here for now.** FeedFabric doesn't limit requests per member yet. Caddy's `rate_limit` plugin,
nginx's `limit_req`, or Cloudflare's rate limiting rules in front of the host can cap writes (`POST`, `PUT`,
`DELETE` under `/userfeed` and `/media`) per client until it does.

</div>

## 6. Migrations

Only Development migrates the database on startup. In production, migrate as a step of every deploy, **before** the
new API and worker start. EF Core's migration bundle is a single executable that does exactly that:

```bash
# On a build machine with the .NET 10 SDK, from the repository root at the version you're deploying
dotnet tool install --global dotnet-ef
dotnet ef migrations bundle --project src/FeedFabric.Application --startup-project src/FeedFabric.Api \
  --self-contained -r linux-x64 -o efbundle

# On the server (or anywhere that can reach the database)
./efbundle --connection "Host=…;Database=feedfabric;Username=feedfabric;Password=…"
```

It applies only the migrations the database doesn't have yet, so it's safe to run on every deploy. If you'd rather
review SQL first, `dotnet ef migrations script --idempotent` writes the same changes as a script.

With the Compose file above, Postgres isn't reachable from outside, so run the bundle in a container on its network:

```bash
docker compose up -d db
docker run --rm --network feedfabric_default -v "$PWD/efbundle:/efbundle:ro" mcr.microsoft.com/dotnet/runtime-deps:10.0 \
  /efbundle --connection "Host=db;Database=feedfabric;Username=feedfabric;Password=$POSTGRES_PASSWORD"
```

## 7. First start

```bash
cd /opt/feedfabric
docker compose pull
docker compose up -d db redis kafka clamav
# … run the migration bundle (above) …
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
4. Open the admin panel's *Settings and models*: it shows the sign-in setup, which models loaded, and the event
   pipeline's lag. A growing outbox means the worker can't reach Kafka.

## Upgrading

```bash
cd /opt/feedfabric
sed -i 's/^FEEDFABRIC_VERSION=.*/FEEDFABRIC_VERSION=1.5.0/' .env
docker compose pull api worker
# Build and run the migration bundle for 1.5.0 first
docker compose up -d api worker
```

Look at what changed under `src/FeedFabric.Application/Migrations` between the two versions before upgrading. Most
migrations add things the old version ignores, so the old API keeps running while you migrate; for one that renames
or drops something, stop the API and worker first. Redis needs nothing; anything in it can be rebuilt.

To roll back, set the previous version and start it again. Migrations aren't rolled back automatically, so roll back
the schema yourself (`./efbundle <previous migration>` goes back to it) only if the new one stops the old version
working.

## Backups

- **Postgres:** everything that matters. Nightly `pg_dump` (or your provider's point-in-time recovery), stored off
  the host, and a restore you've actually tried.
- **The bucket:** members' photos and your trained models. Turn on versioning or replicate it.
- **Redis and Kafka:** no backups needed. Timelines and counters can be rebuilt from Postgres, and unpublished
  events wait in the outbox.

## Scaling

- **API:** stateless apart from caches, so run as many instances as you like behind a load balancer. Each keeps
  the last day's post embeddings and the models in memory.
- **Worker:** several can run side by side. The outbox publisher uses `SKIP LOCKED`, and each consumer group shares
  its topic's partitions, so `Kafka:Partitions` (3 by default) is the most instances that help.
- **Postgres:** the first thing to move to a managed service. Add a connection pooler (PgBouncer) once you run more
  than a few API instances.
- **Kafka:** for a cluster, set a replication factor of 3 and SASL/TLS, and create the topics yourself with
  partitions sized for your load.
- **Kubernetes or a container platform** (ECS, Cloud Run, Fly.io, Azure Container Apps): the same two images and
  the same environment variables, with the migration bundle as a job that runs before each rollout. A worker must
  run continuously, so not on a platform that scales to zero.
