---
layout: ../../layouts/Doc.astro
title: Your own data on posts
description: App tags and typed data. Attach your app's own structured data to posts, checked against a JSON Schema you register, and typed end to end in every SDK.
---

A post can carry more than text. A fitness app can post a member's weight reading, a reading app the book they
finished, and a music app the track they're playing. Each one shows in feeds alongside ordinary posts, and your app
reads it back as its own type. There are two parts: **tags** and **typed data**.

## Tags

Pass `tags` when creating or editing a post:

```json
{ "content": "Down another kilo", "tags": ["weight", "progress"] }
```

App tags are written like hashtags without the `#`: up to 10 per post, each 2–50 letters, digits or `_`, starting
with a letter. They show on the post with `source: "app"`, beside its `hashtag` and `inferred` tags, and work
everywhere tags do: `GET /userfeed/tags/weight/posts`, trending tags, and the For You topics a member engages with.
On an edit, leaving `tags` out keeps the post's.

## Typed data

### 1. Register a type

An [integration admin](/auth/#roles) adds the type in the admin panel (**Integrations → Data types**), or with
`PUT /admin/data-types/{key}`. A type has a key, a label and a [JSON Schema](https://json-schema.org/) for its
values:

```json
{
  "label": "Weight reading",
  "description": "A member's weight, from the scales or typed in",
  "schema": {
    "type": "object",
    "properties": {
      "kg": { "type": "number", "exclusiveMinimum": 0, "maximum": 500 },
      "note": { "type": "string", "maxLength": 200 }
    },
    "required": ["kg"],
    "additionalProperties": false
  }
}
```

Keys are 2–40 lowercase letters, digits and dashes, starting with a letter. The schema's root must be
`"type": "object"`, and the schema can be up to 32 KB. A `$ref` can only point inside the schema: nothing is fetched
from elsewhere.

### 2. Post it

```json
{ "tags": ["weight"], "data": { "type": "weight-reading", "value": { "kg": 82.4 } } }
```

`content` is optional when a post has data. Text moderation only runs when there is text. FeedFabric checks the
value against the schema before saving the post:

| | |
|---|---|
| Unregistered type | `400 post.data_type_unknown` |
| Over 16 KB of JSON | `400 post.data_too_large` |
| Doesn't match | `400 post.data_invalid`, with `errors: [{ "path": "/kg", "message": "…" }]` |

### 3. Read it back

The data comes back on the post in every feed, with the schema version it was checked against:

```json
"data": { "type": "weight-reading", "version": 2, "value": { "kg": 82.4 } }
```

`GET /userfeed?type=weight-reading` lists one member's readings, newest first, with the usual paging and `gt`/`lt`
range on post IDs. It's the member's own history, ready to chart.

### Changing a schema

Saving a schema that differs from the current one bumps its `version`. Saving the same one again doesn't. Old posts
keep the version they were checked against, so readers can tell old values from new ones. Prefer additive changes,
such as new optional properties, so old values still match. Deleting a type stops new posts using it. Posts that
already have it keep their data.

## Type safety

The server's check means bad data never gets in. Each SDK has a `DataType<T>` that makes the same promise in your
code. You define it once with your own type, and from then on it's the only way to build or read that data:

```ts
interface WeightReading { kg: number; note?: string }
const Weight = dataType<WeightReading>('weight-reading')

await member.posts.create({ tags: ['weight'], data: Weight.of({ kg: 82.4 }) }) // only a WeightReading compiles
const reading = Weight.read(post)                                              // WeightReading | undefined
```

```go
var Weight = feedfabric.NewDataType[WeightReading]("weight-reading")
reading, ok, err := Weight.Read(post.Data)
```

```csharp
static readonly DataType<WeightReading> Weight = new("weight-reading");
if (Weight.TryRead(post.Data, out var reading)) { … }
```

Python (`DataType[WeightReading]`), Java (`DataType.define("weight-reading", WeightReading.class)`), PHP
(`@template`, checked by PHPStan or Psalm) and Ruby (RBS, checked by Steep) work the same way. Each SDK's README
shows how.

**Generate the types from the schema** so they can't drift from it. `GET /data-types/{key}/schema` serves the schema
alone, as `application/schema+json`, for standard generators:

| | |
|---|---|
| TypeScript | `json-schema-to-typescript` |
| Python | `datamodel-code-generator --input-file-type jsonschema` |
| Go, C#, Java, Kotlin, Swift | `quicktype --src-lang schema` |
| Java | `jsonschema2pojo` |
| C# | NJsonSchema |

For a check on read as well, give the TypeScript, Python, PHP or Ruby `DataType` a `parse` function. That can be a
zod schema's `parse`, a pydantic model's `model_validate`, or a validator generated from the same schema.
