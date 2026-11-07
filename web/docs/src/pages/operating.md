---
layout: ../layouts/Doc.astro
title: Operating it
description: The settings that matter when running FeedFabric for real, which service reads what, how to deploy it, and what the production-readiness plan leaves open.
---

The repository's `docs/configuration.md` lists every setting and environment variable, `docs/deploying.md` how to
deploy, `docs/runbooks.md` what to do when something needs a person, `docs/data-retention.md` what is kept and for how
long, and `docs/production-readiness.md` where the production plan stands. This page is the short version.

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
| `Topics` | ✓ | | ✓ | `Keywords`: topic name to keywords, replacing the built-in wellness topics; `Enabled` |
| `Feed` | ✓ | ✓ | ✓ | `MaxLength` 500 entries per timeline |
| `Ranking` | ✓ | | ✓ | `DefaultProfile` `balanced` |
| `Kafka` | | ✓ | | `BootstrapServers`, one consumer group per job, `Partitions` 3, `ReplicationFactor` 3, `Security` for SASL/TLS |
| `Outbox` | shown | ✓ | | `MaxPublishAttempts` 10, then a message is parked |
| `Retention` | | ✓ | | How long the outbox, processed events, feed log (180 days), moderation events (a year) and media are kept |
| `RateLimits` | ✓ | | | Per user per minute on writes and signals; `Account` 5 an hour |
| `ForwardedHeaders`, `Cors` | ✓ | | | Behind a TLS proxy; `Cors:AllowedOrigins` for an admin panel on another origin |

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
- `AllowedHosts`, `ForwardedHeaders` behind a proxy, and `Cors:AllowedOrigins` if the admin panel has its own origin
- `Kafka:Security` for managed Kafka
- The safety models (`ml/fetch_models.py --s3-bucket …`, and the fine-tuned toxicity model) and a ranker trained on
  real members, in the bucket

Keep secrets (`ConnectionStrings`, `Auth:Jwt:SigningKey`, `BlobStorage:*:SigningKey`, `BlobStorage:S3:SecretKey`) in
your platform's secret store, passed in as environment variables.

## Deploying

[Deploying FeedFabric](/deploy/) walks through a complete setup, and [Cloudflare](/deploy/cloudflare/) the parts
that can run there. In brief:

- **Images:** CI publishes `api`, `worker-events` and `migrations` to GHCR, tagged `sha-<short>` (and `1.2.3` for a
  release tag), after a Trivy scan. Deploy by tag, not `latest`. The API and the worker listen on 8080.
- **Order:** run the `migrations` image to completion, then roll out the worker, then the API, each instance passing
  `/health/ready` first. Only Development migrates on startup.
- **Stopping:** both finish in-flight work within `ShutdownTimeout` (25 seconds); give containers 30 seconds to stop.
- **Scaling:** API instances keep only caches in memory (the last day's post embeddings, the loaded ranker, users'
  status). Several workers can run side by side: the outbox publisher uses `SKIP LOCKED`, each consumer group shares
  its partitions, and the retention job runs in one at a time. Rate limits are counted per API instance.
- **Your front end:** serve it from the API's origin, or list its origin in `Cors:AllowedOrigins`.
- **The admin panel:** `make admin-build` writes a static site to `web/admin/dist`, to serve from anywhere that
  forwards `/admin`, `/ranking` and `/blobs` (and `/userfeed`, `/media` for its composer) to the API.

## When something goes wrong

- **A dependency is down:** routes that need Postgres, Redis or blob storage answer `503` `service_unavailable` with
  `Retry-After`. The home feed reads Postgres when Redis is down, and the worker retries until the dependency is back.
- **An event keeps failing:** after 5 tries it goes to its topic's `.dead-letter` topic and the partition moves on.
  Fix the cause, then replay it (`docs/runbooks.md`).
- **Members' data:** `GET /me/export` downloads everything held about a member; `DELETE /me` (or
  `DELETE /admin/users/{id}`) erases the account, finishing in the worker within minutes.
- **Staff changes** are logged in the `FeedFabric.Audit` category, with who made them.

## What's still open

From `docs/production-readiness.md`: branch protection on `main`, a DPIA and privacy notice, moving the in-memory
embedding search out of the API, setting a load target for the k6 test in `tools/load`, and a searchable audit table.

## Watching it run

The admin panel's dashboard (`GET /admin/dashboard`) shows activity per minute, top posts and authors, For You
against Following on the same population, exploration against ranked slots, profiles compared on engagement, and what
the safety checks blocked or held. *Settings and models* (`GET /admin/system`) shows the sign-in setup (no secrets),
moderation thresholds, media limits, which models are present, the loaded ranker, users by status, and the event
pipeline: how many outbox messages are waiting, how old the oldest is, and when each consumer last finished one.
