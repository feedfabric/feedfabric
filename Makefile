# Same .env that docker compose reads, so host-side tools use the same credentials
include .env
export ConnectionStrings__Default = Host=localhost;Port=$(POSTGRES_PORT);Database=$(POSTGRES_DB);Username=$(POSTGRES_USER);Password=$(POSTGRES_PASSWORD)

EF = dotnet ef --project src/FeedFabric.Application --startup-project src/FeedFabric.Api

.PHONY: up down down-all logs logs-worker watch psql redis run run-worker simulate simulate-stop migrate migration web web-build train models

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

# Seeds simulated users and follows, then posts every tick; settings in src/FeedFabric.Simulator/appsettings.json
simulate:
	docker compose --profile sim up -d --build simulator
	docker compose logs -f simulator

simulate-stop:
	docker compose --profile sim rm -sf simulator

# Simulator app with hot reload on http://localhost:5173; API calls go to the API on :5161 (override with API_URL)
web:
	cd web/simulator && npm install --no-audit --no-fund && npm run dev

# Builds the simulator app into the API's wwwroot, served at http://localhost:5161/ in Development
web-build:
	cd web/simulator && npm install --no-audit --no-fund && npm run build

# Downloads the pretrained models (nudity, offensive language, text embeddings) into .blobs. Photos and text aren't
# published without the safety models, so run this once before posting.
models:
	cd ml && uv run fetch_models.py

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
