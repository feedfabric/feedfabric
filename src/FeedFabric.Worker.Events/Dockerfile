# Build from the repo root so shared files like Directory.Build.props are in the context:
#   docker build -f src/FeedFabric.Worker.Events/Dockerfile .

# aspnet rather than runtime: the worker serves /health/live and /health/ready on 8080
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# librdkafka looks for the Kerberos library at startup and logs an error without it
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/FeedFabric.Worker.Events/FeedFabric.Worker.Events.csproj", "src/FeedFabric.Worker.Events/"]
COPY ["src/FeedFabric.Application/FeedFabric.Application.csproj", "src/FeedFabric.Application/"]
RUN dotnet restore "src/FeedFabric.Worker.Events/FeedFabric.Worker.Events.csproj"
COPY . .
WORKDIR "/src/src/FeedFabric.Worker.Events"
RUN dotnet build "FeedFabric.Worker.Events.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FeedFabric.Worker.Events.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The images have no curl, so the app checks its own /health/live
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD ["dotnet", "FeedFabric.Worker.Events.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "FeedFabric.Worker.Events.dll"]
