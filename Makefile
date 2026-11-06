# Same .env that docker compose reads, so host-side tools use the same credentials
include .env
export ConnectionStrings__Default = Host=localhost;Port=$(POSTGRES_PORT);Database=$(POSTGRES_DB);Username=$(POSTGRES_USER);Password=$(POSTGRES_PASSWORD)

EF = dotnet ef --project src/FeedFabric.Application --startup-project src/FeedFabric.Api

.PHONY: up down down-all logs logs-worker watch psql redis run run-worker simulate simulate-stop backfill migrate migration admin admin-build fabric train tune-toxicity models

up:
	docker compose up -d --build

# Stops the app containers only; the db, Kafka and Redis stay up
down:
	docker compose --profile sim rm -sf api worker-events simulator

down-all:
	docker compose --profile sim down

logs:
	docker compose logs -f api

logs-worker:
	docker compose logs -f worker-events

watch:
	docker compose watch

psql:
	docker compose exec db psql -U $(POSTGRES_USER) -d $(POSTGRES_DB)

redis:
	docker compose exec redis redis-cli

# Runs the API on the host against the compose db
run:
	dotnet run --project src/FeedFabric.Api

# Runs the worker on the host against the compose db, Kafka and Redis
run-worker:
	dotnet run --project src/FeedFabric.Worker.Events

# Demo data on a fresh database: seeds simulated users and follows and backfills BackfillDays of conversations, likes
# and comments, then stops. Runs on the host, where make models put the safety models, at low priority so the machine
# stays usable. Does nothing to a database that already has posts. Settings in src/FeedFabric.Simulator/appsettings.json.
backfill:
	DOTNET_ENVIRONMENT=Development nice -n 15 dotnet run --project src/FeedFabric.Simulator

# The simulator in Docker; add Simulator__Live=true to keep it posting every tick after the backfill
simulate:
	docker compose --profile sim up -d --build simulator
	docker compose logs -f simulator

simulate-stop:
	docker compose --profile sim rm -sf simulator

# Admin panel with hot reload on http://localhost:5180; API calls go to the API on :5161 (override with API_URL)
admin:
	cd web/admin && npm install --no-audit --no-fund && npm run dev

# Builds the admin panel as a static site into web/admin/dist, to serve from anywhere that forwards /admin, /ranking
# and /blobs to the API
admin-build:
	cd web/admin && npm install --no-audit --no-fund && npm run build

# FabricUI's gallery: every component in every state, on http://localhost:5190
fabric:
	cd web/fabric-ui && npm install --no-audit --no-fund && npm run dev

# Downloads the pretrained models (nudity, offensive language, text embeddings) into .blobs. Photos and text aren't
# published without the safety models, so run this once before posting.
models:
	cd ml && uv run fetch_models.py

# Fine-tunes the offensive-language model on this app's wording ("kill it at the race" isn't a threat) and publishes
# it to .blobs over the one make models fetched; restart the API to load it. Downloads about 1 GB the first time and
# takes about 45 minutes on an Apple-silicon Mac. It won't publish a model that fails its tests (ml/tune_toxicity.py).
tune-toxicity:
	cd ml && uv run --group tune tune_toxicity.py

# Trains the For You ranker from the feed log and publishes it to .blobs; the API loads it within a minute.
# Needs uv (brew install uv), on a Mac LightGBM's OpenMP runtime (brew install libomp), and some feed history:
# let the simulator run for a while first.
train:
	cd ml && uv run train_ranker.py --source all
# Production trains on real members only: uv run train_ranker.py --source real (the default)

migrate:
	$(EF) database update

# make migration name=AddPosts
migration:
	$(EF) migrations add $(name) --output-dir Migrations
