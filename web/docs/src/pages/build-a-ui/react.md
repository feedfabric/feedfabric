---
layout: ../../layouts/Doc.astro
title: React and plain JavaScript
description: The same member app without Vue. A framework-free core for paging and reading signals, React hooks on top of it, a plain script that needs no build step, and what changes when you render on a server.
---

The rest of this section builds the member app in Vue with FabricUI, but nothing in the API is Vue-specific: it's
JSON over HTTP with a bearer token. This page has the same app in React, and the parts in plain JavaScript, so you
can bring FeedFabric into whatever you already have.

**Reuse the [API client](/build-a-ui/api-client/) as it is.** Its types, `ApiError`, `describe`, `request` and
`api` are plain TypeScript with no framework in them; copy them into `src/api/` and `src/types.ts`. The only
Vite-specific line is `import.meta.env.DEV` in `auth.ts`; elsewhere, replace it with your own "is this development?"
check.

## A framework-free core

Two pieces hold the logic that's easy to get wrong. Neither knows about React, so the hooks below stay thin, and a
plain page (or Svelte, Angular, a web component) can use them directly.

### Paging a feed

The rules from [Feeds and paging](/build-a-ui/feeds/): pass `nextCursor` back unchanged, stop when it's `null` (not
when a page is short), never show a post twice, and let a refresh cancel an older page that's still loading.

```ts
// src/core/pager.ts
import { describe } from '../api/errors'
import type { FeedPost } from '../types'

export interface Page { posts: FeedPost[]; nextCursor: string | null }
export interface PagerState { posts: FeedPost[]; loading: boolean; done: boolean; error: string | null }

export function createPager(load: (cursor: string | null) => Promise<Page>, onChange: (state: PagerState) => void) {
  let state: PagerState = { posts: [], loading: false, done: false, error: null }
  let cursor: string | null = null
  let generation = 0
  const set = (next: Partial<PagerState>) => onChange((state = { ...state, ...next }))

  async function fetchPage(reset: boolean) {
    if (!reset && (state.loading || state.done)) return
    const mine = ++generation
    set({ loading: true, error: null })
    try {
      const page = await load(reset ? null : cursor)
      if (mine !== generation) return
      const seen = new Set(reset ? [] : state.posts.map(p => p.id))
      const fresh = page.posts.filter(p => !seen.has(p.id))
      cursor = page.nextCursor
      set({ posts: reset ? fresh : [...state.posts, ...fresh], done: page.nextCursor === null, loading: false })
    } catch (e) {
      if (mine === generation) set({ error: describe(e), loading: false })
    }
  }

  return {
    refresh: () => fetchPage(true),
    more: () => fetchPage(false),
    // After "Not interested" or deleting: take it off the screen without reloading
    remove: (id: string) => set({ posts: state.posts.filter(p => p.id !== id && p.repostOf?.id !== id) }),
  }
}
```

### Reporting what's read

The rules from [Reading signals](/build-a-ui/signals/): an impression is at least half on screen for a second,
dwell is sent when a view ends, everything is batched, and the last batch goes out with `keepalive` when the page is
hidden. `watchPost` watches one element and returns a function that stops watching.

```ts
// src/core/reading.ts
import { api } from '../api'

const VisibleFor = 1000      // an impression: at least half on screen for a second
const MinDwell = 300         // shorter than this is scrolling past
const FlushEvery = 3000
const MaxBatch = 100         // the API's limit per request

const seen = new Set<string>()
const dwell: { postId: string; ms: number }[] = []
let timer: ReturnType<typeof setTimeout> | undefined

function flush(closing = false) {
  clearTimeout(timer)
  timer = undefined
  while (seen.size || dwell.length) {
    const ids = [...seen].slice(0, MaxBatch)
    ids.forEach(id => seen.delete(id))
    const views = dwell.splice(0, MaxBatch)
    if (ids.length) api.impressions(ids, closing).catch(() => {})
    if (views.length) api.dwell(views, closing).catch(() => {})
    if (!closing) break
  }
  if (seen.size || dwell.length) schedule()
}
const schedule = () => void (timer ??= setTimeout(() => flush(), FlushEvery))

document.addEventListener('visibilitychange', () => document.visibilityState === 'hidden' && flush(true))
window.addEventListener('pagehide', () => flush(true))

// postId: the post the member sees, which for a repost is repostOf.id
export function watchPost(el: Element, postId: string, root: Element | null = null): () => void {
  let counted = false
  let intersecting = false
  let since: number | null = null
  let impression: ReturnType<typeof setTimeout> | undefined

  const start = () => {
    if (document.visibilityState === 'hidden' || since !== null) return
    since = performance.now()
    if (!counted) impression = setTimeout(() => { counted = true; seen.add(postId); schedule() }, VisibleFor)
  }
  const stop = () => {
    clearTimeout(impression)
    if (since === null) return
    const ms = Math.round(performance.now() - since)
    since = null
    if (ms >= MinDwell) { dwell.push({ postId, ms }); schedule() }
  }
  const onVisibility = () => (document.visibilityState === 'hidden' ? stop() : intersecting && start())

  const observer = new IntersectionObserver(([entry]) => {
    intersecting = entry.isIntersecting
    intersecting ? start() : stop()
  }, { root, threshold: 0.5 })
  observer.observe(el)
  document.addEventListener('visibilitychange', onVisibility)

  return () => {
    stop()
    observer.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
  }
}
```

## React

### Set up

```bash
npm create vite@latest app -- --template react-ts
cd app
```

Proxy the member routes to the API in development, as in the Vue guide, so the app and API share an origin (the API
sends CORS headers only for origins in `Cors:AllowedOrigins`):

```ts
// vite.config.ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 5161 is `make run`; the API container from `make up` is on 8080
const api = process.env.API_URL ?? 'http://localhost:5161'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5187,
    strictPort: true,
    proxy: { '/userfeed': api, '/follows': api, '/favourites': api, '/media': api, '/blobs': api },
  },
})
```

### Hooks

```tsx
// src/hooks.ts
import { useEffect, useMemo, useRef, useState, type RefObject } from 'react'
import { createPager, type Page, type PagerState } from './core/pager'
import { watchPost } from './core/reading'

// key: change it (a profile, a tag) to start the feed again from the top
export function useFeed(load: (cursor: string | null) => Promise<Page>, key = '') {
  const [state, setState] = useState<PagerState>({ posts: [], loading: false, done: false, error: null })
  const loadRef = useRef(load)
  loadRef.current = load
  const pager = useMemo(() => createPager(cursor => loadRef.current(cursor), setState), [key])
  useEffect(() => void pager.refresh(), [pager])
  return { ...state, ...pager }
}

export function useImpression(ref: RefObject<Element | null>, postId: string) {
  useEffect(() => (ref.current ? watchPost(ref.current, postId) : undefined), [ref, postId])
}

// Calls onNear when the element comes within 800px of the viewport
export function useNearViewport(ref: RefObject<Element | null>, onNear: () => void) {
  const callback = useRef(onNear)
  callback.current = onNear
  useEffect(() => {
    if (!ref.current) return
    const observer = new IntersectionObserver(([e]) => e.isIntersecting && callback.current(), { rootMargin: '800px 0px' })
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [ref])
}
```

### A post card

```tsx
// src/PostCard.tsx
import { useRef, useState } from 'react'
import { api } from './api'
import { describe } from './api/errors'
import { useImpression } from './hooks'
import type { FeedPost } from './types'

export function PostCard({ post, onRemove }: { post: FeedPost; onRemove: (id: string) => void }) {
  // A repost shows, and reports signals for, the post it shares
  const shown = post.repostOf ?? post
  const ref = useRef<HTMLElement>(null)
  useImpression(ref, shown.id)

  // Starts from what the feed said; optimistic after that, since PUT and DELETE are idempotent
  const [like, setLike] = useState({ on: shown.liked, count: shown.likes })
  const [error, setError] = useState<string | null>(null)

  async function toggleLike() {
    const was = like
    setLike({ on: !was.on, count: was.count + (was.on ? -1 : 1) })
    try { await (was.on ? api.unlike(shown.id) : api.like(shown.id)) }
    catch (e) { setLike(was); setError(describe(e)) }
  }

  async function notInterested() {
    onRemove(shown.id)                  // the session's order doesn't change until the next first page
    await api.notInterested(shown.id).catch(() => {})
  }

  return (
    <article ref={ref} className="post" aria-labelledby={`by-${post.id}`}>
      {post.repostOf && <p className="reposted">Reposted by @{post.authorUsername}</p>}
      <header id={`by-${post.id}`}>@{shown.authorUsername}</header>
      {shown.isHeld && <p className="held">Only you can see this while it's reviewed.</p>}
      <p>{shown.content}</p>
      {shown.media.map(m => (
        <img key={m.id} src={m.thumbnailUrl} width={m.width} height={m.height} alt="" loading="lazy" />
      ))}
      <footer>
        <button aria-pressed={like.on} onClick={toggleLike}>{like.on ? 'Liked' : 'Like'} · {like.count}</button>
        {post.ranking && <button onClick={notInterested}>Not interested</button>}
      </footer>
      {post.ranking && (
        <details>
          <summary>Why this post?</summary>
          <ul>{post.ranking.factors.map(f => <li key={f.label}><strong>{f.label}</strong> {f.detail}</li>)}</ul>
        </details>
      )}
      {error && <p role="alert">{error}</p>}
    </article>
  )
}
```

[Post cards and actions](/build-a-ui/post-cards/) covers the rest of the card (tags and mentions as links, photos
whose URLs expire, comments, held posts, and a fuller *Why this post?*); all of it carries over directly.

### The feed

```tsx
// src/Home.tsx
import { useRef, useState } from 'react'
import { api } from './api'
import { useFeed, useNearViewport } from './hooks'
import { PostCard } from './PostCard'

const tabs = [
  { key: 'foryou', label: 'For you' },
  { key: 'discovery', label: 'Discover' },     // any ranking profile key makes a tab
  { key: 'following', label: 'Following' },
] as const

export function Home() {
  const [tab, setTab] = useState<(typeof tabs)[number]['key']>('foryou')
  const feed = useFeed(cursor =>
    tab === 'following' ? api.home(cursor)
    : api.forYou(cursor, tab === 'foryou' ? null : tab), tab)

  const sentinel = useRef<HTMLDivElement>(null)
  useNearViewport(sentinel, () => !feed.error && feed.more())

  return (
    <main>
      <nav role="tablist" aria-label="Feeds">
        {tabs.map(t => (
          <button key={t.key} role="tab" aria-selected={tab === t.key} onClick={() => setTab(t.key)}>{t.label}</button>
        ))}
        <button onClick={feed.refresh} disabled={feed.loading}>Refresh</button>
      </nav>

      <section aria-busy={feed.loading}>
        {feed.posts.map(p => <PostCard key={p.id} post={p} onRemove={feed.remove} />)}
        {feed.done && !feed.posts.length && <p>Nothing here yet.</p>}
        {feed.error && <p role="alert">{feed.error} <button onClick={feed.more}>Try again</button></p>}
        <div ref={sentinel} aria-hidden="true" />
        {!feed.done && feed.posts.length > 0 && <button onClick={feed.more} disabled={feed.loading}>Load more</button>}
      </section>
    </main>
  )
}
```

Switching tabs changes `key`, which builds a new pager and loads the new feed from the top. To keep each tab's place
when switching back, render one `Feed` component per tab and hide the inactive ones instead.

<div class="note">

**Styling.** FabricUI is Vue components, but its design tokens are plain CSS. Import
`web/fabric-ui/src/styles/fabric.css` and put `class="fabric"` on `body`, and you can use the same colours, spacing
and type (`var(--fabric-accent)`, `var(--fabric-space-4)`), light and dark, as the admin panel and this site.

</div>

## Plain JavaScript

No framework and no build step: a For You list with a *Load more* button, served from the same origin as the API.
This needs the API client compiled to JavaScript (or written without types) next to it.

```html
<!doctype html>
<html lang="en-GB">
  <body>
    <main id="feed" aria-busy="false"></main>
    <button id="more">Load more</button>
    <script type="module">
      import { createPager } from './core/pager.js'
      import { watchPost } from './core/reading.js'
      import { api } from './api/index.js'

      const feed = document.getElementById('feed')
      const more = document.getElementById('more')
      const watching = new Map()   // post id -> stop watching

      const pager = createPager(cursor => api.forYou(cursor), state => {
        feed.setAttribute('aria-busy', String(state.loading))
        more.hidden = state.done
        for (const post of state.posts) {
          if (watching.has(post.id)) continue
          const shown = post.repostOf ?? post
          const card = document.createElement('article')
          card.innerHTML = `<header></header><p></p>`
          card.querySelector('header').textContent = `@${shown.authorUsername}`
          card.querySelector('p').textContent = shown.content   // textContent: never innerHTML with post text
          feed.append(card)
          watching.set(post.id, watchPost(card, shown.id))
        }
      })

      more.addEventListener('click', () => pager.more())
      pager.refresh()
    </script>
  </body>
</html>
```

Post text, usernames and tags are what members typed: always set them with `textContent` (or your framework's
escaping), never as HTML.

## Rendering on a server

Next.js, Remix, Astro, Rails or anything else can render the first page of a feed on the server:

- **Call the API with the member's token**, from the server, the same way the browser would. FeedFabric doesn't care
  where a request comes from, only whose token it carries.
- **Don't cache feed responses** between members, or at all: For You is per member, and every first page ranks
  again and starts a new 15-minute session.
- **Hand the cursor to the browser** with the first page, and fetch later pages from there.
- **Report reading from the browser.** Impressions and dwell are about what was actually on screen, which only the
  browser knows. A server-rendered page that never runs `watchPost` teaches the ranker nothing.
- **Signed photo URLs expire** (an hour by default), so render them per request, never into a cached page.

## Native apps

iOS, Android, Flutter and React Native apps follow the same contract: the endpoints in the
[API reference](/api/), errors by `code`, and reading signals with the same rules. Use the platform's visibility
callbacks (a list's "item became visible" events) for the half-on-screen-for-a-second impression rule, and flush
the queue when the app goes to the background. CORS doesn't apply to native apps, so they can call the API's own
origin directly.
