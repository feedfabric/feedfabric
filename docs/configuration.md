# Configuration

Every setting FeedFabric reads, which service reads it, and how to set it from the environment.

## How settings are read

The API, the worker and the simulator are ordinary .NET apps. Each reads, later sources winning:

1. `appsettings.json` in the app's folder
2. `appsettings.<Environment>.json` (`appsettings.Development.json` in this repo)
3. Environment variables
4. Command-line arguments

The environment is `ASPNETCORE_ENVIRONMENT` for the API and `DOTNET_ENVIRONMENT` for the worker and the simulator.
Anything other than `Development` is treated as production. Development is the only environment where:

- `Auth:Mode` can be `Dev` (any token, or `X-Dev-User`, is trusted), and any signed-in user is staff in the admin panel
- the API, the worker and the simulator migrate the database on startup, and the API seeds dev users
- the API serves its OpenAPI document

### As environment variables

Replace each `:` with a double underscore. List items and dictionary keys are just one more level:

| Setting | Environment variable |
|---|---|
| `Auth:Jwt:Audience` | `Auth__Jwt__Audience` |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` |
| `Auth:Roles:Moderator` (a list) | `Auth__Roles__Moderator__0`, `Auth__Roles__Moderator__1`, … |
| `Moderation:ToxicityHoldAt` (a dictionary) | `Moderation__ToxicityHoldAt__identity_attack=0.7` |

Durations are `hh:mm:ss` (`00:15:00`), or `d.hh:mm:ss` for days.

### Lists and dictionaries add to their defaults

Where a setting's default is a non-empty list or dictionary, what you configure is added to it rather than replacing
it:

- `Moderation:ToxicityRejectAt`, `ToxicityHoldAt` and `ToxicityFlagAt`: a key you set overrides that label's
  threshold, but the default labels stay. To stop a default label acting, set its threshold above 1 (scores run from
  0 to 1), e.g. `Moderation__ToxicityHoldAt__sexual_explicit=1.1`.
- `Moderation:ToxicityOnlyWhenAddressed`: labels you list are added to `insult`, which can't be removed from
  configuration.
- `Simulator:ForYouProfiles`: profiles you list are added after the built-in `balanced`.

The `Auth` lists are the exception: they start empty and only fall back to their defaults when you leave them empty,
so the roles and claims you list replace the defaults.

## Which service reads what

| Section | API | Worker | Simulator |
|---|:-:|:-:|:-:|
| `ConnectionStrings` | ✓ | ✓ | ✓ |
| `Auth` | ✓ | | |
| `BlobStorage` | ✓ | ✓ | ✓ |
| `Media` | ✓ | ✓ | |
| `Moderation` | ✓ | ✓ | ✓ |
| `MalwareScanning` | ✓ (shown in the admin panel) | ✓ | |
| `Feed` | ✓ | ✓ | ✓ |
| `Ranking` | ✓ | | ✓ |
| `Kafka` | | ✓ | |
| `Simulator` | | | ✓ |

Settings that more than one service reads must have the same value in each. For example, the API checks
`Media:MaxUploadBytes` when it hands out an upload URL and the worker checks it again, and the admin panel shows the
API's `Moderation` and `MalwareScanning` values while the worker acts on its own.

## Connection strings

| Setting | Default | Notes |
|---|---|---|
| `ConnectionStrings:Default` | none | PostgreSQL, e.g. `Host=db;Port=5432;Database=feedfabric;Username=…;Password=…`. Required. Secret. |
| `ConnectionStrings:Redis` | `localhost:6379` | Home timelines. The API starts without Redis and falls back to Postgres; the worker needs it. |

## Auth

How FeedFabric checks tokens from your identity provider, and where in them it finds the user and their roles.
[auth.md](auth.md) explains each setting and has examples for Auth0, Entra ID, Cognito, Keycloak and your own
OpenID Connect server.

| Setting | Default | Notes |
|---|---|---|
| `Auth:Mode` | `Jwt` | `Dev` trusts any token without checking it and refuses to start outside Development |
| `Auth:Jwt:Authority` | none | OpenID Connect issuer. Use this or `SigningKey`. |
| `Auth:Jwt:Audience` | none | Required in `Jwt` mode |
| `Auth:Jwt:SigningKey` | none | HS256 shared secret, for a host that signs its own tokens. Secret. |
| `Auth:Jwt:Issuer` | none | The `iss` tokens signed with `SigningKey` carry |
| `Auth:Jwt:ClockSkew` | `00:02:00` | |
| `Auth:Jwt:RequireHttpsMetadata` | `true` | Turn off only for an identity provider on localhost |
| `Auth:Claims:UserId` | `sub` | Must never change for a person |
| `Auth:Claims:Username` | `preferred_username`, `nickname`, `username` | First one present wins |
| `Auth:Claims:DisplayName` | `name` | |
| `Auth:Claims:Avatar` | `picture` | |
| `Auth:Claims:Roles` | `roles`, `role`, `permissions` | |
| `Auth:Roles:Moderator` | `moderator` | Your role names that count as moderators |
| `Auth:Roles:RankingAdmin` | `ranking-admin` | Your role names that can save ranking profiles |
| `Auth:ProvisionUsers` | `true` | Create users on their first request |
| `Auth:ProfileRefresh` | `01:00:00` | How long a profile from a token is trusted before it's refreshed |

## Blob storage

Uploads, rebuilt images and the models.

| Setting | Default | Notes |
|---|---|---|
| `BlobStorage:Provider` | `Local` | `Local` or `S3`. Use `S3` outside Development. |
| `BlobStorage:Local:Root` | `blobs` | Folder for the files. A relative path is relative to the build output (`bin/Debug/net10.0`). The API and the worker must share it. |
| `BlobStorage:Local:PublicBaseUrl` | `http://localhost:5161` | Where the API serves `/blobs`. Empty gives relative URLs. |
| `BlobStorage:Local:SigningKey` | none | Signs read and upload URLs. Any long random string. Secret. |
| `BlobStorage:Local:MaxUploadBytes` | `20971520` (20 MB) | Enforced by the API's local upload endpoint. Keep it in step with `Media:MaxUploadBytes`. |
| `BlobStorage:S3:Bucket` | none | |
| `BlobStorage:S3:Region` | `us-east-1` | |
| `BlobStorage:S3:ServiceUrl` | none | Empty for AWS. Set for LocalStack, R2, MinIO, Spaces. |
| `BlobStorage:S3:PublicServiceUrl` | none | The host clients use in signed URLs, when it differs from `ServiceUrl` |
| `BlobStorage:S3:AccessKey` | none | Empty uses the default AWS credential chain (environment, profile, IAM role). Secret. |
| `BlobStorage:S3:SecretKey` | none | Secret |
| `BlobStorage:S3:ForcePathStyle` | `false` | Most S3-compatible services need `true` |
| `BlobStorage:S3:CreateBucket` | `false` | Create the bucket on startup. For local emulators only. |

## Media

Photo uploads. Allowed types are fixed in code: JPEG, PNG and WebP.

| Setting | Default | Notes |
|---|---|---|
| `Media:MaxUploadBytes` | `20971520` (20 MB) | Keep under clamd's `StreamMaxLength` (25 MB by default) so every upload can be scanned |
| `Media:MaxDimension` | `12000` | Longest edge, read from the header before decoding |
| `Media:MaxPixels` | `50000000` | Also checked before decoding |
| `Media:LargeEdge` | `2048` | Longest edge of the rebuilt image |
| `Media:ThumbnailEdge` | `400` | |
| `Media:MaxPerPost` | `4` | |
| `Media:UploadUrlLifetime` | `00:15:00` | |
| `Media:ReadUrlLifetime` | `01:00:00` | |

## Moderation

| Setting | Default | Notes |
|---|---|---|
| `Moderation:BlockedTerms` | empty | Words and phrases a post or comment can't contain, matched whole and ignoring case. Keep the real list out of the repo. |
| `Moderation:MaxLinks` | `2` | More links than this is spam |
| `Moderation:MaxRepeatedCharacters` | `12` | A character repeated this often is flooding |
| `Moderation:ToxicityRejectAt` | `threat` 0.6 | Refused outright |
| `Moderation:ToxicityHoldAt` | `severe_toxicity` 0.5, `identity_attack` 0.6, `sexual_explicit` 0.9 | Held: seen only by the author until a moderator decides |
| `Moderation:ToxicityFlagAt` | `insult` 0.9 | Published, and put in the review queue |
| `Moderation:ToxicityOnlyWhenAddressed` | `insult` | Labels that only act when the text speaks to someone |
| `Moderation:ReportsToHold` | `3` | Reports from different people that hold a post or comment |
| `Moderation:NudityRejectAt` | `0.8` | Photos scored at or above this are rejected |

`ml/tune_toxicity.py` tests the model against the toxicity thresholds, so keep its `LIMITS` in step when you change
them.

## Malware scanning

| Setting | Default | Notes |
|---|---|---|
| `MalwareScanning:Enabled` | `true` | Off publishes photos without a ClamAV scan (they're still type-checked and rebuilt), so clamd needn't run |
| `MalwareScanning:ClamAv:Host` | `localhost` | |
| `MalwareScanning:ClamAv:Port` | `3310` | |
| `MalwareScanning:ClamAv:ChunkBytes` | `65536` | |
| `MalwareScanning:ClamAv:Timeout` | `00:01:00` | |

## Feeds and ranking

| Setting | Default | Notes |
|---|---|---|
| `Feed:MaxLength` | `500` | Newest entries kept per Redis timeline; older posts load from Postgres |
| `Ranking:DefaultProfile` | `balanced` | The ranking profile For You uses when a request doesn't name one: a preset or a saved profile's key |

## Kafka (worker)

| Setting | Default | Notes |
|---|---|---|
| `Kafka:BootstrapServers` | `localhost:9092` | |
| `Kafka:GroupId` | `worker-events` | Fan-out |
| `Kafka:EngagementGroupId` | `worker-engagement` | |
| `Kafka:MediaGroupId` | `worker-media` | |
| `Kafka:EmbeddingsGroupId` | `worker-embeddings` | |
| `Kafka:Partitions` | `3` | Used when the worker creates missing topics on startup |

## Simulator

`Simulator:*` sets the simulated population and how it behaves: how many users and follows, how often they post, read,
like and comment, the backfill on an empty platform, whether it keeps running live after the backfill (`Simulator:Live`,
off by default), and the random seed. Each setting is described in
`src/FeedFabric.Simulator/SimulatorOptions.cs`, and the defaults are in the simulator's `appsettings.json`. The
simulator never runs in production.

## Hosting

Standard ASP.NET Core settings that matter here:

| Setting | Notes |
|---|---|
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | See above |
| `ASPNETCORE_HTTP_PORTS` or `ASPNETCORE_URLS` | The API container listens on 8080; `dotnet run` uses 5161 from `launchSettings.json` |
| `AllowedHosts` | `*` in `appsettings.json`. Set to your API's host names in production. |
| `Logging:LogLevel:*` | Log levels by category |

## Docker Compose

`docker-compose.yml` reads `.env` (copy `.env.example`):

| Variable | Default | Notes |
|---|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | none | Used for the database and the services' connection strings |
| `POSTGRES_PORT` | `5432` | Port on the host |
| `MALWARE_SCANNING_ENABLED` | `true` | Passed to the API and the worker as `MalwareScanning__Enabled` |

## Tools

| Variable | Used by | Default |
|---|---|---|
| `API_URL` | The admin panel's dev server (`make admin`), for proxying API calls | `http://localhost:5161` |
| `DATABASE_URL` | `ml/train_ranker.py` | `postgresql://feedfabric:feedfabric@localhost:5432/feedfabric` |

## Production checklist

- `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` not `Development`
- `ConnectionStrings:Default` and `ConnectionStrings:Redis`
- `Auth:Jwt:Audience`, plus `Auth:Jwt:Authority` (or `SigningKey` and `Issuer`), and `Auth:Roles` mapped to your roles
- `BlobStorage:Provider` `S3` with a bucket and credentials
- `Kafka:BootstrapServers`
- `MalwareScanning:ClamAv:Host`, or `MalwareScanning:Enabled` `false`
- `Moderation:BlockedTerms`
- `AllowedHosts`

Keep secrets (`ConnectionStrings`, `Auth:Jwt:SigningKey`, `BlobStorage:*:SigningKey`, `BlobStorage:S3:SecretKey`) in
your platform's secret store and pass them in as environment variables, never in `appsettings` files.
