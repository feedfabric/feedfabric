# Build from the repo root so shared files like Directory.Build.props are in the context:
#   docker build -f src/FeedFabric.Api/Dockerfile -t feedfabric-api .

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
# Security fixes Ubuntu has published since Microsoft last rebuilt the base image (CI's Trivy scan fails on them)
RUN apt-get update && apt-get upgrade -y --no-install-recommends && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/FeedFabric.Api/FeedFabric.Api.csproj", "src/FeedFabric.Api/"]
COPY ["src/FeedFabric.Application/FeedFabric.Application.csproj", "src/FeedFabric.Application/"]
RUN dotnet restore "src/FeedFabric.Api/FeedFabric.Api.csproj"
COPY . .
WORKDIR "/src/src/FeedFabric.Api"
RUN dotnet build "FeedFabric.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FeedFabric.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The images have no curl, so the app checks its own /health/live
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD ["dotnet", "FeedFabric.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "FeedFabric.Api.dll"]
