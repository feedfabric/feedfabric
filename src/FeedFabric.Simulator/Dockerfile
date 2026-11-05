# Build from the repo root so shared files like Directory.Build.props are in the context:
#   docker build -f src/FeedFabric.Simulator/Dockerfile .

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/FeedFabric.Simulator/FeedFabric.Simulator.csproj", "src/FeedFabric.Simulator/"]
COPY ["src/FeedFabric.Application/FeedFabric.Application.csproj", "src/FeedFabric.Application/"]
RUN dotnet restore "src/FeedFabric.Simulator/FeedFabric.Simulator.csproj"
COPY . .
WORKDIR "/src/src/FeedFabric.Simulator"
RUN dotnet build "FeedFabric.Simulator.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FeedFabric.Simulator.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FeedFabric.Simulator.dll"]
