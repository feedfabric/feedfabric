import assert from 'node:assert/strict'
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http'
import type { AddressInfo } from 'node:net'
import { after, before, beforeEach, test } from 'node:test'
import { FeedFabric, FeedFabricError, dataType, type Post } from '../dist/index.js'

// A stand-in API: records each request and answers with whatever the test queued
interface Seen { method: string; url: string; headers: IncomingMessage['headers']; body: string }
type Reply = { status: number; body?: unknown; headers?: Record<string, string> }
const seen: Seen[] = []
let replies: Reply[] = []
let baseUrl = ''

const server = createServer((req: IncomingMessage, res: ServerResponse) => {
  let body = ''
  req.on('data', chunk => (body += chunk))
  req.on('end', () => {
    seen.push({ method: req.method!, url: req.url!, headers: req.headers, body })
    const reply = replies.shift() ?? { status: 204 }
    res.writeHead(reply.status, { 'Content-Type': reply.status >= 400 ? 'application/problem+json' : 'application/json', ...reply.headers })
    res.end(reply.body === undefined ? '' : JSON.stringify(reply.body))
  })
})

before(async () => {
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`
})
after(() => server.close())
beforeEach(() => {
  seen.length = 0
  replies = []
})

const client = () => new FeedFabric({ baseUrl, apiKey: 'ffk_test', maxRetries: 2 })

interface WeightReading { kg: number; note?: string }
const Weight = dataType<WeightReading>('weight-reading')

test('acts for the member with the key and percent-encoded profile headers', async () => {
  replies.push({ status: 200, body: { externalUserId: 'user 1' } })

  const me = await client().asMember('user 1', { username: 'zoe', displayName: 'Zoë Brontë' }).me()

  assert.equal(me.externalUserId, 'user 1')
  const [request] = seen
  assert.equal(request.method, 'GET')
  assert.equal(request.url, '/me')
  assert.equal(request.headers.authorization, 'Bearer ffk_test')
  assert.equal(request.headers['feedfabric-user'], 'user%201')
  assert.equal(decodeURIComponent(request.headers['feedfabric-display-name'] as string), 'Zoë Brontë')
})

test('posts typed data and app tags, and reads the data back typed', async () => {
  const saved = { id: 'p1', authorId: 'a1', content: '', createdAt: '2026-11-10T09:00:00Z', editedAt: null, repostOfId: null, isHeld: false,
    data: { type: 'weight-reading', version: 2, value: { kg: 82.4 } } }
  replies.push({ status: 201, body: saved })

  const post = await client().asMember('u1').posts.create({ tags: ['weight'], data: Weight.of({ kg: 82.4 }) })

  assert.deepEqual(JSON.parse(seen[0].body), { tags: ['weight'], data: { type: 'weight-reading', value: { kg: 82.4 } } })
  assert.equal(seen[0].headers['content-type'], 'application/json')
  const reading: WeightReading | undefined = Weight.read(post)
  assert.equal(reading?.kg, 82.4)
  assert.equal(dataType<{ steps: number }>('steps').read(post), undefined)
})

test('a parser checks the data it reads', () => {
  const Strict = dataType<WeightReading>('weight-reading', value => {
    if (typeof (value as WeightReading).kg !== 'number') throw new TypeError('kg must be a number')
    return value as WeightReading
  })
  const post = { data: { type: 'weight-reading', version: 1, value: { kg: 'eighty' } } } as unknown as Post
  assert.throws(() => Strict.read(post), /kg must be a number/)
})

test('builds query strings and leaves out what is unset', async () => {
  replies.push({ status: 200, body: { posts: [], nextCursor: null } })

  await client().asMember('u1').posts.list({ type: 'weight-reading', limit: 5, cursor: undefined })

  assert.equal(seen[0].url, '/userfeed?type=weight-reading&limit=5')
})

test('errors carry the code, params and field errors', async () => {
  replies.push({ status: 400, body: { status: 400, code: 'post.data_invalid', detail: 'nope', params: { type: 'weight-reading' },
    errors: [{ path: '/kg', message: 'Value is "string" but should be "number"' }], traceId: 't-1' } })

  const error = await client().asMember('u1').posts.create({ data: Weight.of({ kg: 1 }) }).catch(e => e)

  assert.ok(error instanceof FeedFabricError)
  assert.equal(error.status, 400)
  assert.equal(error.code, 'post.data_invalid')
  assert.deepEqual(error.params, { type: 'weight-reading' })
  assert.equal((error.errors[0] as { path: string }).path, '/kg')
  assert.equal(error.traceId, 't-1')
})

test('waits out a 429 and tries again, even for a POST', async () => {
  replies.push({ status: 429, body: { code: 'rate_limited', params: { retryAfterSeconds: 0 } }, headers: { 'Retry-After': '0' } })
  replies.push({ status: 202, body: { counted: 2 } })

  const result = await client().asMember('u1').signals.impressions(['p1', 'p2'])

  assert.equal(result.counted, 2)
  assert.equal(seen.length, 2)
})

test('does not repeat a POST after a 503, but does a GET', async () => {
  replies.push({ status: 503, body: { code: 'service_unavailable' }, headers: { 'Retry-After': '0' } })
  const post = await client().asMember('u1').posts.create({ content: 'hi' }).catch(e => e)
  assert.equal(post.code, 'service_unavailable')
  assert.equal(seen.length, 1)

  replies.push({ status: 503, body: { code: 'service_unavailable' }, headers: { 'Retry-After': '0' } }, { status: 200, body: [] })
  await client().asMember('u1').favourites()
  assert.equal(seen.length, 3)
})

test('uploads an image and waits until it is ready', async () => {
  replies.push(
    { status: 201, body: { mediaId: 'm1', uploadUrl: `${baseUrl}/blobs/m1`, contentType: 'image/png', expiresAt: '2026-11-10T10:00:00Z' } },
    { status: 200 },
    { status: 202, body: { id: 'm1', status: 'processing' } },
    { status: 200, body: { id: 'm1', status: 'ready', url: 'u', thumbnailUrl: 't', width: 1, height: 1, rejectionReason: null } },
  )

  const media = await client().asMember('u1').media.upload(new Uint8Array([1, 2, 3]), 'image/png', { pollMs: 1 })

  assert.equal(media.status, 'ready')
  assert.deepEqual(seen.map(s => `${s.method} ${s.url}`), ['POST /media/uploads', 'PUT /blobs/m1', 'POST /media/m1/complete', 'GET /media/m1'])
  assert.equal(seen[1].headers.authorization, undefined, 'the upload URL is signed; the key stays off it')
  assert.deepEqual(JSON.parse(seen[0].body), { contentType: 'image/png', size: 3 })
})

test('refuses a missing or wrong-looking key up front', () => {
  assert.throws(() => new FeedFabric({ baseUrl, apiKey: 'eyJhbGciOi...' }), /API key/)
})
