# The database migrations as a one-off container, run before each API rollout (docs/deploying.md):
#   docker build -f src/FeedFabric.Application/Dockerfile -t feedfabric-migrations .
#   docker run --rm -e ConnectionStrings__Default="Host=...;Database=...;Username=...;Password=..." feedfabric-migrations
# It applies any migrations the database doesn't have yet, each in its own transaction, and exits non-zero on
# failure. Running it again is a no-op.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY ["Directory.Build.props", "global.json", "dotnet-tools.json", "./"]
COPY ["src/FeedFabric.Application/FeedFabric.Application.csproj", "src/FeedFabric.Application/"]
RUN dotnet tool restore && dotnet restore "src/FeedFabric.Application/FeedFabric.Application.csproj"
COPY . .
RUN dotnet ef migrations bundle --project src/FeedFabric.Application --startup-project src/FeedFabric.Application \
        --self-contained -r linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) -o /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0 AS final
# Security fixes Ubuntu has published since Microsoft last rebuilt the base image (CI's Trivy scan fails on them)
RUN apt-get update && apt-get upgrade -y --no-install-recommends && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
COPY --from=build /out/efbundle .
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$ConnectionStrings__Default\""]
