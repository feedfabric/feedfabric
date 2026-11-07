---
layout: ../layouts/Doc.astro
title: Operating it
description: The settings that matter when running FeedFabric for real, which service reads what, and what the production-readiness plan says is still to do.
---

The repository's `docs/configuration.md` lists every setting and environment variable, and
`docs/production-readiness.md` the plan for running it for real members. This page is the short version.

## How settings are read

The API, worker and simulator are ordinary .NET apps: `appsettings.json`, then `appsettings.<Environment>.json`, then
environment variables, then the command line, later ones winning. Environment variables replace each `:` with `__`
(`Auth__Jwt__Audience`, `Auth__Roles__Moderator__0`). Durations are `hh:mm:ss`.

Anything but `Development` (`ASPNETCORE_ENVIRONMENT` for the API, `DOTNET_ENVIRONMENT` for the worker) is production.
**Only Development** allows `Auth:Mode` `Dev`, lets any signed-in user act as staff, migrates the database and seeds
dev users on startup, and serves the OpenAPI document.

## Which service reads what

| Section | API | Worker | Simulator | Highlights |
|---|:-:|:-:|:-:|---|
| `ConnectionStrings` | ✓ | ✓ | ✓ | `Default` (Postgres, required, secret); `Redis` (the API starts without it and falls back to Postgres; the worker needs it) |
| `Auth` | ✓ | | | `Mode`, `Jwt:Authority`/`Audience`, claim and role mapping ([Sign-in and users](/auth/)) |
| `BlobStorage` | ✓ | ✓ | ✓ | `Provider` `Local` or `S3`; the API and worker must share the store |
| `Media` | ✓ | ✓ | | `MaxUploadBytes` 20 MB, `MaxPerPost` 4, URL lifetimes |
| `Moderation` | ✓ | ✓ | ✓ | `BlockedTerms`, toxicity thresholds, `ReportsToHold` 3, `NudityRejectAt` 0.8 |
| `MalwareScanning` | shown | ✓ | | `Enabled`, `ClamAv:Host`/`Port` |
| `Feed` | ✓ | ✓ | ✓ | `MaxLength` 500 entries per timeline |
| `Ranking` | ✓ | | ✓ | `DefaultProfile` `balanced` |
| `Kafka` | | ✓ | | `BootstrapServers`, one consumer group per job, `Partitions` 3 |

Settings more than one service reads must match in each: the API checks `Media:MaxUploadBytes` when it hands out an
upload URL and the worker checks it again; the admin panel shows the API's `Moderation` and `MalwareScanning` values
while the worker acts on its own.

Where a default is a non-empty list or dictionary (the toxicity thresholds, `ToxicityOnlyWhenAddressed`,
`Simulator:ForYouProfiles`), what you configure is **added** to it; to switch off a default toxicity label, set its
threshold above 1. The `Auth` lists are the exception: yours **replace** the defaults.

## Production checklist

From `docs/configuration.md`:

- `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` not `Development`
- `ConnectionStrings:Default` and `ConnectionStrings:Redis`
- `Auth:Jwt:Audience`, plus `Auth:Jwt:Authority` (or `SigningKey` and `Issuer`), and `Auth:Roles` mapped to your roles
- `BlobStorage:Provider` `S3` with a bucket and credentials, and a CORS rule for your app's origin
- `Kafka:BootstrapServers`
- `MalwareScanning:ClamAv:Host`, or `MalwareScanning:Enabled` `false`
- `Moderation:BlockedTerms` (keep the real list out of the repository)
- `AllowedHosts`
- The safety models (`ml/fetch_models.py --s3-bucket …`, and the fine-tuned toxicity model) and a ranker trained on
  real members, in the bucket

Keep secrets (`ConnectionStrings`, `Auth:Jwt:SigningKey`, `BlobStorage:*:SigningKey`, `BlobStorage:S3:SecretKey`) in
your platform's secret store, passed in as environment variables.

## Deploying

[Deploying FeedFabric](/deploy/) walks through a complete setup, and [Cloudflare](/deploy/cloudflare/) the parts
that can run there. In brief:

- **Images:** `src/FeedFabric.Api/Dockerfile` and `src/FeedFabric.Worker.Events/Dockerfile`, built from the repository
  root. The API listens on 8080 in its container. The decided target is Docker, from images CI publishes to GHCR.
- **Migrations:** only Development migrates on startup. Elsewhere run them as a deploy step before the API rolls out
  (`make migrate` locally; an EF migration bundle in CI is the plan).
- **Scaling:** API instances keep only caches in memory (the last day's post embeddings, the loaded ranker, users' status). Several
  workers can run side by side: the outbox publisher uses `SKIP LOCKED`, and each consumer group shares its
  partitions.
- **Your front end:** the API sends no CORS headers, so serve it from the same origin as the API (or add a policy).
- **The admin panel:** `make admin-build` writes a static site to `web/admin/dist`, to serve from anywhere that
  forwards `/admin`, `/ranking` and `/blobs` (and `/userfeed`, `/media` for its composer) to the API.

## What the plan says is still to do

`docs/production-readiness.md` tracks it in priority order. In brief:

| Priority | Item |
|---|---|
| P0 | Land work in progress; CI on GitHub Actions (done, branch protection to add); remove the `/weatherforecast` template route |
| P0 | Length caps on post and comment text; request body limits |
| P0 | Rate limiting per user on writes, signals and previews, returning `429` with a stable code |
| P0 | Worker failure safety: a maximum attempt count and a dead-letter topic, so one poison message can't stall a partition |
| P0 | `/health/live` and `/health/ready` on the API and worker |
| P0 | Migrations as a deploy step; fail fast on missing configuration; `ForwardedHeaders`, HSTS and CORS |
| P1 | OpenTelemetry traces and metrics (outbox and consumer lag, ranker fallback rate), structured logs |
| P1 | Retention jobs for the outbox, processed events, `FeedLog`, moderation events, abandoned media and Redis keys that never expire |
| P1 | GDPR: account deletion across Postgres, Redis, blobs and `FeedLog`; subject access export |
| P1 | Resilience: retries and timeouts for S3 and ClamAV, defined behaviour when Redis is down on every path |
| P1 | Versioned, checksummed model publishing; refuse a ranker trained on simulated data outside Development |
| P2 | Move the in-memory embedding search to pgvector or Redis; cache the dashboard; load tests |
| P2 | Kafka replication and TLS; security headers; an audit log of admin actions; runbooks |

## Watching it run

The admin panel's dashboard (`GET /admin/dashboard`) shows activity per minute, top posts and authors, For You
against Following on the same population, exploration against ranked slots, profiles compared on engagement, and what
the safety checks blocked or held. *Settings and models* (`GET /admin/system`) shows the sign-in setup (no secrets),
moderation thresholds, media limits, which models are present, the loaded ranker, users by status, and the event
pipeline: how many outbox messages are waiting, how old the oldest is, and when each consumer last finished one.
