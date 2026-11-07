// Load test for the feeds members hit most: Following (GET /userfeed/home) and For You (GET /userfeed/foryou),
// with the impressions a client sends as it scrolls. Run with k6 (https://k6.io) against a staging API:
//
//   k6 run -e BASE_URL=https://staging.example.com -e TOKENS=tokens.txt tools/load/feeds.js
//
// TOKENS is a file of bearer tokens, one per line, for members who already follow people (sign each in once first,
// so FeedFabric has provisioned them). Each virtual user takes one. The target isn't decided yet
// (docs/production-readiness.md, item 16), so RPS and P95_MS are settings: the run fails if p95 goes over P95_MS or
// more than 1% of requests fail. Don't point it at production: every request writes to the feed log.
import http from 'k6/http'
import { check, sleep } from 'k6'
import { SharedArray } from 'k6/data'

const baseUrl = __ENV.BASE_URL || 'http://localhost:5161'
const rps = Number(__ENV.RPS || 50)
const p95 = Number(__ENV.P95_MS || 300)
const tokens = new SharedArray('tokens', () =>
  open(__ENV.TOKENS || 'tokens.txt').split('\n').map(t => t.trim()).filter(Boolean))

export const options = {
  scenarios: {
    feeds: {
      executor: 'constant-arrival-rate',
      rate: rps,
      timeUnit: '1s',
      duration: __ENV.DURATION || '5m',
      preAllocatedVUs: Math.max(10, rps),
      maxVUs: Math.max(50, rps * 4),
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{feed:home}': [`p(95)<${p95}`],
    'http_req_duration{feed:foryou}': [`p(95)<${p95}`],
  },
}

export default function () {
  const headers = { Authorization: `Bearer ${tokens[__VU % tokens.length]}`, 'Content-Type': 'application/json' }
  // Members open For You more than Following
  const feed = Math.random() < 0.7 ? 'foryou' : 'home'
  const response = http.get(`${baseUrl}/userfeed/${feed}?limit=20`, { headers, tags: { feed } })
  check(response, { 'feed is 200': r => r.status === 200 })
  if (response.status !== 200) {
    return
  }

  const posts = response.json('posts') || []
  if (posts.length > 0) {
    http.post(`${baseUrl}/userfeed/impressions`, JSON.stringify({ postIds: posts.slice(0, 10).map(p => p.id) }),
      { headers, tags: { feed: 'signals' } })
  }
  sleep(Math.random())
}
