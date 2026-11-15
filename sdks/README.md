# FeedFabric SDKs

Libraries for calling FeedFabric from your own servers, for your members. Your app signs its users in as it always
has. Its server holds an API key and calls FeedFabric on a member's behalf: posting, reading feeds, liking,
commenting, uploading photos and sending reading signals.

| Language | Folder | Package | Needs |
|---|---|---|---|
| TypeScript / JavaScript | [`typescript`](typescript) | `@feedfabric/sdk` | Node 18+, Deno, Bun or Workers (uses `fetch`) |
| Python | [`python`](python) | `feedfabric` | Python 3.9+, no dependencies |
| Go | [`go`](go) | `github.com/feedfabric/feedfabric/sdks/go` | Go 1.22+, no dependencies |
| C# / .NET | [`dotnet`](dotnet) | `FeedFabric.Sdk` | .NET 10, no dependencies |
| Java / Kotlin | [`java`](java) | `io.feedfabric:feedfabric-java` | Java 17+, Jackson |
| PHP | [`php`](php) | `feedfabric/feedfabric-php` | PHP 8.1+, ext-curl |
| Ruby | [`ruby`](ruby) | `feedfabric` | Ruby 3.1+, no dependencies |

They all work the same way, with the names each language expects. Each folder's README has its own quick start.

## How it fits together

1. **Deploy FeedFabric against your sign-in server** ([docs/auth.md](../docs/auth.md)). Your staff sign in to the
   admin panel with their usual accounts.
2. **Give someone the `integration-admin` role** (`Auth:Roles:IntegrationAdmin`). In the admin panel they get an
   *Integrations* section.
3. **Make an API key** (Integrations → API keys) for each server that will call FeedFabric. The key (`ffk_…`) is
   shown once. Keep it in that server's secret store. Revoking a key stops it within a minute.
4. **Call FeedFabric for a member.** The key says *which server* is calling, and the member's ID says *who for*:

```ts
const feed = new FeedFabric({ baseUrl: 'https://feed.example.com', apiKey: process.env.FEEDFABRIC_API_KEY! })
const member = feed.asMember(user.id, { username: user.handle, displayName: user.name })
await member.posts.create({ content: 'First run of the year #running' })
```

The member's ID is the one your sign-in tokens carry (`Auth:Claims:UserId`, `sub` by default), so a member is the
same person whether they reach FeedFabric through your server or through a token of their own. FeedFabric creates
the member the first time it sees them, using the profile you pass, and refreshes the profile at most hourly.

On the wire, each call is `Authorization: Bearer ffk_…` plus `FeedFabric-User: <id>`. The profile goes in
`FeedFabric-Username`, `FeedFabric-Display-Name` and `FeedFabric-Avatar`, all percent-encoded. An API key can only do
what a member can. It is refused on every staff route, even if the member has staff roles.

## Your own data on posts: tags and typed data

Posts can carry your app's own data, not just text. A fitness app might post weight readings, so we'll use that as
the example.

**Tags.** Pass `tags` and they are added to the post's tags with source `app`. Write them like hashtags without the
`#`: up to 10, each 2–50 letters, digits or `_`, starting with a letter. They work everywhere hashtags do: `feeds.tag("weight")`, trending
tags and For You topics. Leaving `tags` out of an edit keeps the post's tags.

**Typed data.** An integration admin registers a *data type* (Integrations → Data types). It has a key, such as
`weight-reading`, and a JSON Schema for its values:

```json
{
  "type": "object",
  "properties": {
    "kg": { "type": "number", "exclusiveMinimum": 0, "maximum": 500 },
    "note": { "type": "string", "maxLength": 200 }
  },
  "required": ["kg"],
  "additionalProperties": false
}
```

A post then carries `data: { type: "weight-reading", value: { kg: 82.4 } }`, with or without text. FeedFabric checks
every value against the schema. A value that fails is refused with `400 post.data_invalid`, and its `errors` lists
each `{ path, message }`. An unregistered type gets `post.data_type_unknown`, and over 16 KB gets
`post.data_too_large`. The data comes back on the post in every feed, with the schema `version` it was checked
against. `posts.list(type: "weight-reading")` gives a member's own readings, newest first.

**Type safety, end to end.** The server checks the shape, so bad data never gets in. Each SDK has a `DataType<T>`
that makes the same promise in your code. You define it once with your own type, and it is the only way to build or
read that data:

| | Define | Post | Read |
|---|---|---|---|
| TypeScript | `dataType<WeightReading>('weight-reading', parse?)` | `data: Weight.of({ kg: 82.4 })` | `Weight.read(post)` → `WeightReading \| undefined` |
| Python | `DataType[WeightReading]("weight-reading", parse=, dump=)` | `data=Weight.of(reading)` | `Weight.read(post)` → `WeightReading \| None` |
| Go | `feedfabric.NewDataType[WeightReading]("weight-reading")` | `Data: Weight.Of(reading)` | `Weight.Read(post.Data)` → `(WeightReading, bool, error)` |
| C# | `new DataType<WeightReading>("weight-reading")` | `Data = Weight.Of(reading)` | `Weight.TryRead(post.Data, out var reading)` |
| Java | `DataType.define("weight-reading", WeightReading.class)` | `PostInput.data(WEIGHT.of(reading))` | `WEIGHT.read(post.data())` → `Optional<WeightReading>` |
| PHP | `/** @var DataType<WeightReading> */ new DataType('weight-reading', parse:, dump:)` | `data: $weight->of($reading)` | `$weight->read($post)` → `?WeightReading` (PHPStan/Psalm) |
| Ruby | `DataType.new("weight-reading", parse:, dump:)` typed `DataType[WeightReading]` in RBS | `data: WEIGHT.of(reading)` | `WEIGHT.read(post)` → `WeightReading?` (Steep) |

To keep your types in step with the schema, generate them from it. Every type's schema is served at
`GET /data-types/{key}/schema` as plain JSON Schema, so standard generators work:
`json-schema-to-typescript`, `datamodel-code-generator` (Python), `quicktype` (Go, C#, Java, Kotlin, Swift and
more), `jsonschema2pojo` (Java) and NJsonSchema (C#). In TypeScript, pass a validator (for example a zod schema's
`parse`) as `parse` to check values on read as well.

Changing a schema bumps its version. Posts keep the version they were checked against, so readers can tell old values
from new ones. Make additive changes (new optional fields) where you can, and the old posts stay valid.

## Errors and retries

Every failure is a `FeedFabricError` (`FeedFabric::Error` in Ruby, `FeedFabricException` in C#, Java and PHP). It
carries the HTTP `status`, a stable `code` from [docs/errors.md](../docs/errors.md), `params`, `errors`, `traceId`
and `retryAfter`. Match on `code`, never on the message. With no response at all, the code is `network_error` and
the status is 0.

The SDKs retry for you, up to `maxRetries` times (default 2):

- **429:** always retried, after `Retry-After`. Nothing happened on the server, so this is safe even for a POST.
- **503:** retried after `Retry-After` for GET, PUT and DELETE, which are safe to repeat.
- **Network errors:** retried with backoff for GET, PUT and DELETE.

A POST that fails part-way is never repeated, so a post is never made twice.

## Photos

`media.upload(bytes, contentType)` does the whole upload:

1. It asks for an upload URL.
2. It sends the file there directly (to the storage, without your API key).
3. It waits for the image to be scanned, then returns it, ready for a post's `mediaIds`.

A refused image raises `media.rejected`. One still being checked after a minute raises `media.timeout`.

## Testing

Each SDK's tests run it against a stand-in HTTP server:

| | |
|---|---|
| TypeScript | `npm test` |
| Python | `uv run pytest && uv run mypy` |
| Go | `go test ./...` |
| C# | `dotnet test --project tests/FeedFabric.Sdk.Tests` |
| Java | `mvn test` |
| PHP | `composer test` (PHPStan at max, then PHPUnit) |
| Ruby | `rake` (Minitest, then `rbs validate`) |
