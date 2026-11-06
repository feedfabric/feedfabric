---
layout: ../../layouts/Doc.astro
title: Reading signals
description: Impressions, dwell, opens, Not interested and Show more are how For You learns. What each one means, when to send it, and a composable that reports them correctly.
---

## Why it matters

For You can only learn from what your app tells it. Likes and comments arrive through their own endpoints, but
everything about **reading** comes from the client:

| Signal | Endpoint | Feeds |
|---|---|---|
| Impression | `POST /userfeed/impressions` `{ postIds }` | `Seen` on the feed log (**rows that are never seen aren't training data at all**); the `impressions`, `ctr` and `engagement_rate` signals; the "seen already" penalty |
| Dwell | `POST /userfeed/dwell` `{ views: [{ postId, ms }] }` | The `long_dwell` (10 s or more) and `skip` labels; the `avg_dwell_s` signal and the *Holds attention* factor |
| Open | `POST /userfeed/posts/{id}/open` | The `open` label; `opens`, `ctr` and the viewer-author click-through; *This session* |
| Not interested | `POST /userfeed/posts/{id}/not-interested` | Hides the post for 30 days, lowers its author and topics for this member, and the `skip` label |
| Show more | `POST /userfeed/posts/{id}/show-more` | Raises its author and topics for this member; kept as a label |

An app that skips these still works, but its members' For You never gets better, the feed's quality signals stay at
their priors, and posts they've already read keep coming back at full strength. Every one of them is cheap: batched,
best effort, and accepted even from suspended members.

## What each one means

- **Impression**: the post was **at least half on screen for a second**. Count it once per post per screen; the
  server ignores repeats from the same member within 30 minutes anyway. Never on render, never for a post in a
  background tab or under a dialog.
- **Dwell**: how long a post stayed at least half on screen, **sent each time it scrolls away** (or the tab is
  hidden). Several views of the same post add up. The server caps a single view at 60 seconds (someone who left the
  tab open isn't reading) and ignores zero.
- **Open**: the member opened the post's own page or thread (tapped the card, the comments, a photo). Repeats within
  30 minutes count once.
- **Not interested / Show more**: explicit buttons. Each is a state, not a counter; pressing the other one undoes it.

**Always name the post the member actually sees**: for a repost, `repostOf.id`. The feed log records the original as
served, so signals about the repost's own ID label nothing.

<div class="note">

**Labels need the serve.** Signals label feed log rows for that post **served to that member in the last hour**. A
post they reach only by a link, a tag page or mentions has no row to label (those lists aren't logged), so its
signals still count towards the post's totals but don't become training rows.

</div>

## A composable that gets it right

One module-level queue for the whole app, flushed every few seconds and whenever the page is hidden; one observer per
post card. This is the old member app's `useImpressions.ts`, updated for today's API.

```ts
// src/composables/useImpression.ts
import { onBeforeUnmount, onMounted, type Ref } from 'vue'
import { api } from '../api'

const VisibleFor = 1000      // an impression: at least half on screen for a second
const MinDwell = 300         // shorter than this is scrolling past, not a view
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
    // Best effort: a lost batch costs a little training data, never an error on screen.
    // keepalive lets the request finish after the page is gone (sendBeacon can't send an Authorization header).
    if (ids.length) api.impressions(ids, closing).catch(() => {})
    if (views.length) api.dwell(views, closing).catch(() => {})
    if (!closing) break
  }
  if (seen.size || dwell.length) schedule()
}

function schedule() {
  timer ??= setTimeout(() => flush(), FlushEvery)
}

// Leaving or hiding the page sends what's waiting, so the last posts' dwell isn't lost
document.addEventListener('visibilitychange', () => document.visibilityState === 'hidden' && flush(true))
window.addEventListener('pagehide', () => flush(true))

// Watches one post. target: the card's element, or a component whose root element it is.
// postId: the post the member sees (repostOf.id for a repost).
export function useImpression(target: Ref<HTMLElement | { $el: HTMLElement } | undefined>, postId: () => string,
  root: Element | null = null) {
  let impressionTimer: ReturnType<typeof setTimeout> | undefined
  let counted = false
  let visibleSince: number | null = null
  let observer: IntersectionObserver | undefined

  function start() {
    if (document.visibilityState === 'hidden' || visibleSince !== null) return
    visibleSince = performance.now()
    if (!counted) {
      impressionTimer = setTimeout(() => {
        counted = true
        seen.add(postId())
        schedule()
      }, VisibleFor)
    }
  }

  function stop() {
    clearTimeout(impressionTimer)
    if (visibleSince === null) return
    const ms = Math.round(performance.now() - visibleSince)
    visibleSince = null
    if (ms >= MinDwell) {
      dwell.push({ postId: postId(), ms })
      schedule()
    }
  }

  let intersecting = false
  const onVisibility = () => (document.visibilityState === 'hidden' ? stop() : intersecting && start())

  onMounted(() => {
    const el = target.value instanceof HTMLElement ? target.value : target.value?.$el
    if (!el) return
    observer = new IntersectionObserver(([entry]) => {
      intersecting = entry.isIntersecting
      intersecting ? start() : stop()
    }, { root, threshold: 0.5 })
    observer.observe(el)
    document.addEventListener('visibilitychange', onVisibility)
  })

  onBeforeUnmount(() => {
    stop()
    observer?.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
  })
}
```

Use it in the post card with the original's ID, as in [Post cards](/build-a-ui/post-cards/#the-card):

```ts
const root = ref<InstanceType<typeof FabricCard>>()
useImpression(root, () => (props.post.repostOf ?? props.post).id)
```

If your feed scrolls inside its own element rather than the page, pass that element as `root`. If a dialog or
drawer covers the feed, stop the observers (unmount, or call `stop` for each) while it's open.

## Opens

Report an open when the member goes into a post: its thread, its photos, its own page. Not when a card merely
expands in place for a long text, and not on navigating *back* to it.

```ts
// On the post page, or wherever "opening a post" happens in your design
onMounted(() => api.open(post.id).catch(() => {}))   // 202 { counted: true } the first time in 30 minutes
```

## Not interested and Show more

Offer both on For You posts, typically in the card's overflow menu. After *Not interested*, take the post off the
screen yourself: the session's order doesn't change until the next first page. After either, a short toast ("You'll
see less like this") tells the member it worked.

```ts
await api.notInterested(post.id)    // 204; 404 post.not_found if the post is gone entirely
feed.remove(post.id)
```

*Show more* only works on posts everyone can still see; *Not interested* works on anything, including a post held or
removed since it was served.

## Limits and responses

| Call | Limit | Response |
|---|---|---|
| `/userfeed/impressions` | Up to 100 `postIds` per request | `202 { "counted": n }`: how many weren't repeats |
| `/userfeed/dwell` | Up to 100 `views`; `ms` 0 or more | `202 { "counted": n }`: views with time on them |
| `/userfeed/posts/{id}/open` | | `202 { "counted": true }` or `false` |
| `/userfeed/posts/{id}/not-interested`, `/show-more` | | `204`, or `404 post.not_found` |

Over 100 items is `400 validation_failed` with `out_of_range` on the field, so batch.

## Common mistakes

- **Counting on render.** A post rendered below the fold hasn't been seen. Use an observer and the one-second rule.
- **Sending the repost's ID.** The feed log has the original; use `repostOf.id`.
- **Losing the last batch.** Flush on `visibilitychange`/`pagehide` with `keepalive`, or the dwell of the last posts
  read (often the most telling) never arrives.
- **Counting background tabs.** Stop timing when `document.visibilityState` is `hidden`.
- **Sending dwell twice.** Dwell adds up on the server; send each view once, when it ends.
- **Re-ranking on a timer.** Every For You first page ranks again; refresh when the member asks.

<div class="warn">

**A gap to know about.** The trainer labels a seen post as a **skip** when it had under a second of dwell and no
engagement. A browser app following the one-second impression rule can't produce that combination (a post counted as
seen has already had at least a second on screen), so from real clients the `skip` label currently comes only from
*Not interested*. The simulator reports reading directly and does produce quick skips. Keep to the documented
contract; this is for whoever tunes the trainer's labels.

</div>
