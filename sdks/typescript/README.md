# FeedFabric for TypeScript

Call FeedFabric from your servers, for your members, with an API key. It uses `fetch`, so it runs on Node 18+, Deno,
Bun and Workers. It has no dependencies. [../README.md](../README.md) covers API keys, tags and typed data for every
SDK.

```sh
npm install @feedfabric/sdk
```

```ts
import { FeedFabric, FeedFabricError, dataType } from '@feedfabric/sdk'

const feed = new FeedFabric({ baseUrl: 'https://feed.example.com', apiKey: process.env.FEEDFABRIC_API_KEY! })
const member = feed.asMember(user.id, { username: user.handle, displayName: user.name })

const page = await member.feeds.forYou({ limit: 20 })
await member.posts.like(page.posts[0].id)
```

## Typed data

```ts
interface WeightReading { kg: number; note?: string }
const Weight = dataType<WeightReading>('weight-reading')

await member.posts.create({ tags: ['weight'], data: Weight.of({ kg: 82.4 }) })

const { posts } = await member.posts.list({ type: Weight.key })
for (const post of posts) {
  const reading = Weight.read(post) // WeightReading | undefined
}
```

`Weight.of` only takes a `WeightReading`, and `Weight.read` gives one back. To check values on read too, pass a
validator: `dataType('weight-reading', WeightSchema.parse)` with zod, or any function that throws on a bad value.
To generate the interface from the registered schema, run
`npx json-schema-to-typescript <(curl -H "Authorization: Bearer $KEY" -H "FeedFabric-User: any" $BASE/data-types/weight-reading/schema)`.

## Errors

```ts
try {
  await member.posts.create({ content })
} catch (e) {
  if (e instanceof FeedFabricError && e.code === 'content.violation') showRules(e.params.category)
  else throw e
}
```

## Tests

`npm test` builds the package and runs it against a stand-in server.
