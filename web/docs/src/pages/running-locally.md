---
layout: ../layouts/Doc.astro
title: Running it locally
description: Docker for the infrastructure, the .NET services, the simulator for data, and a trained ranker, on one machine.
---

## What you need

- Docker (Compose v2)
- The .NET 10 SDK (`global.json` pins it)
- Node 22 or later, for the admin panel, FabricUI and this site
- [uv](https://docs.astral.sh/uv/) for the Python in `ml/`; on a Mac, `brew install libomp` too before training

## Quick start

```bash
cp .env.example .env
make models      # download the safety and embedding models into .blobs (once)
make up          # Postgres, Kafka, Redis, ClamAV, API and worker in Docker
make simulate    # seed users, backfill 14 days of conversations, then run live
make admin       # the admin panel at http://localhost:5185
```

In Development the API migrates the database on startup and seeds three dev users (`1`, `2`, `3`, named `dev1` to
`dev3`). Any request without a token acts as user `1`; send `X-Dev-User: 2` to be someone else
([Sign-in and users](/auth/)).

<div class="warn">

**Containers or the host?** `make up` runs the API in a container on **port 8080**, while `make admin` (and any
Vite dev server set up like it) proxies to **5161**, the port `make run` uses. Either run the API on the host, or
start the dev server with `API_URL=http://localhost:8080`.

The containers also keep blobs in a Docker volume (`blobs:/blobs`), not the `.blobs` folder that `make models` and
`make train` write to, so a containerised API and worker won't see those models. For the whole loop (safety
models, photos, a trained ranker), run the infrastructure in Docker and the .NET services on the host, as below.

</div>

### Infrastructure in Docker, services on the host

```bash
docker compose up -d --wait db kafka redis   # what Rider's "FeedFabric: Infra" runs
docker compose up -d clamav                  # optional: see "Malware scanning" below
make run                                     # API on http://localhost:5161
make run-worker                              # outbox publisher and Kafka consumers
dotnet run --project src/FeedFabric.Simulator
```

Each `dotnet run` uses the project's `appsettings.Development.json`: Postgres and Redis on localhost, and the repo's
`.blobs` folder for blob storage, shared by all three. In Rider, the *FeedFabric: All* run configuration does all of
this in one go.

## Ports

| Port | What |
|---|---|
| 5161 | API from `make run` / `dotnet run` (7164 for the `https` profile) |
| 8080 | API in Docker (`make up`) |
| 5185 | Admin panel dev server (`make admin`) |
| 5186 | This documentation site (`npm run dev` in `web/docs`) |
| 5190 | FabricUI gallery (`make fabric`) |
| 5173, 5174 | Taken by the old simulator UIs; keep new dev servers off them |
| 5432 | PostgreSQL (`POSTGRES_PORT` in `.env`) |
| 9092 | Kafka from the host (containers use `kafka:29092`) |
| 6379 | Redis |
| 3310 | ClamAV |
| 4566 | LocalStack S3, only with `docker compose --profile s3 up -d localstack` |

## Make targets

| Command | What it does |
|---|---|
| `make up` / `make down` / `make down-all` | Start everything; stop the app containers; stop everything (data is kept) |
| `make run` / `make run-worker` | Run the API or worker on your machine against Compose |
| `make simulate` / `make simulate-stop` | Start or stop the simulator container (profile `sim`) |
| `make admin` / `make admin-build` | Admin panel with hot reload on 5185; or build it into `web/admin/dist` |
| `make fabric` | FabricUI's gallery: every component in every state, on 5190 |
| `make models` | Download the pretrained safety and embedding models into `.blobs/models` |
| `make tune-toxicity` | Fine-tune the offensive-language model on this app's wording (about 45 minutes, 1 GB download) |
| `make train` | Train the For You ranker from the feed log and publish it as ONNX |
| `make migration name=AddX` / `make migrate` | Create or apply an EF Core migration |
| `make logs` / `make logs-worker` / `make psql` / `make redis` | Logs and shells |

## The simulator

On an empty database the simulator backfills 14 days of history through the real services (moderation, tagging,
events and fan-out all run), then every second five posts go up and ten members read their feeds and like, comment,
open, press *Not interested* or *Show more*, with probabilities that fall with position in the feed. Its users are
`sim-1` to `sim-1000` (`sim-1` the most followed). **Even-numbered users read For You, odd-numbered ones Following**,
so the dashboard can compare the two on the same population.

All of this is `Simulator:*` in `src/FeedFabric.Simulator/appsettings.json`; `ForYouProfiles` splits For You
readers between several [ranking profiles](/how-it-works/profiles/).

<div class="tip">

**Try the API as a simulated member.** In Development, `X-Dev-User: sim-42` makes you `sim-42`, with their follows,
likes and history:

```bash
curl -s -H 'X-Dev-User: sim-42' 'http://localhost:5161/userfeed/foryou?limit=5' | jq '.posts[].ranking.factors'
```

</div>

## Models

`make models` (`ml/fetch_models.py`) downloads three pinned models into `.blobs/models`:

| Job | Model | Runs in |
|---|---|---|
| Offensive language | `unitary/unbiased-toxic-roberta` (8-bit ONNX) | API |
| Nudity | `Falconsai/nsfw_image_detection` (8-bit ONNX) | Worker |
| Meaning similarity | `all-MiniLM-L6-v2` | Worker (the API reads the vectors back from Redis) |

**Text and photos aren't published without the safety models**: the checks fail closed. As published, the toxicity
model reads sport talk as violence ("kill it at the race"), so `make tune-toxicity` fine-tunes it on this app's
wording and publishes it over the downloaded one only if it passes its tests. Restart the API to load it.

The fourth model, the For You ranker, you train yourself.

## Training the ranker

Let the simulator run for a while (the trainer refuses fewer than 2,000 seen posts from the last 7 days), then:

```bash
make train       # cd ml && uv run train_ranker.py --source all
```

It trains one LightGBM model per outcome, checks them on the newest 20% of the data against the built-in formula,
exports them to ONNX and writes `models/ranker/latest.json` into `.blobs`. **The API checks for a new model once a
minute**; until it has one, For You uses the formula. `--source all` includes simulated users, fine for development;
the default, `--source real`, is what production trains on. See [Feed log and training](/how-it-works/training/).

## Malware scanning

ClamAV scans every upload before anything decodes it. Its first start downloads the virus database (a few minutes);
until it's healthy, uploads wait in quarantine. Set `MALWARE_SCANNING_ENABLED=false` in `.env` (or
`MalwareScanning__Enabled=false` for host-run services) to publish photos without a scan; they're still type-checked
and rebuilt.

## The OpenAPI document

In Development the API serves its OpenAPI document at
[`http://localhost:5161/openapi/v1.json`](http://localhost:5161/openapi/v1.json). The admin panel's *API
reference* page shows a saved copy of it (refresh it with `make openapi`). The [API reference](/api/) here groups the same endpoints by what
they're for.

## This site

```bash
cd web/docs
npm install
npm run dev      # http://localhost:5186
npm run build    # static site and search index in web/docs/dist
```

[Contributing to the docs](/contributing/) explains how it's put together. When you're ready to run FeedFabric for real members, go on to [Deploying FeedFabric](/deploy/).
