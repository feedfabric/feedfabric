---
layout: ../../layouts/Doc.astro
title: Feeds and paging
description: Following and For You as tabs, a page at a time with cursors, infinite scroll that never shows a post twice, and refreshing without reshuffling what someone is reading.
---

## How the two feeds page

| | Following (`/userfeed/home`) | For You (`/userfeed/foryou`) |
|---|---|---|
| Order | Newest first | Ranked, then arranged for variety |
| `nextCursor` | The last post's ID | An offset: `"20"`, `"40"`… |
| First page | The newest posts | **Ranks again** and starts a new 15-minute session |
| Later pages | Older than the cursor | The same session's order, from the offset |
| Page size | `limit` (1–100, default 20); fewer only at the end | Can be **fewer than `limit` before the end**, when posts were removed since ranking |
| End | `nextCursor: null` | `nextCursor: null` |
| Extras | `source` | `ranker`, `profile`, `candidates`, and `ranking` on every post |

Treat cursors as opaque strings, pass them back unchanged, and stop when `nextCursor` is `null`, not when a page is
short. For You's session can expire mid-scroll, in which case the next page comes from a freshly ranked list, so
**always drop posts you've already shown, by `id`**.

## A feed composable

One composable serves both, and any other paged list (favourites, a tag, mentions):

```ts
// src/composables/useFeed.ts
import { ref, shallowRef } from 'vue'
import { describe } from '../api/errors'
import type { FeedPost } from '../types'

export interface Page { posts: FeedPost[]; nextCursor: string | null }

// load(cursor) fetches one page; null cursor means "from the top"
export function useFeed<P extends Page>(load: (cursor: string | null) => Promise<P>) {
  const posts = shallowRef<FeedPost[]>([])   // replaced, never changed in place
  const last = shallowRef<P | null>(null)    // the latest page, for ranker/profile and the like
  const cursor = ref<string | null>(null)
  const done = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)
  let generation = 0                         // a refresh makes in-flight older pages stale

  async function fetchPage(reset: boolean) {
    if (loading.value && !reset) return
    if (!reset && done.value) return
    const mine = ++generation
    loading.value = true
    error.value = null
    try {
      const page = await load(reset ? null : cursor.value)
      if (mine !== generation) return
      const seen = new Set(reset ? [] : posts.value.map(p => p.id))
      const fresh = page.posts.filter(p => !seen.has(p.id))
      posts.value = reset ? fresh : [...posts.value, ...fresh]
      last.value = page
      cursor.value = page.nextCursor
      done.value = page.nextCursor === null
    } catch (e) {
      if (mine === generation) error.value = describe(e)
    } finally {
      if (mine === generation) loading.value = false
    }
  }

  return {
    posts, last, done, loading, error,
    refresh: () => fetchPage(true),
    more: () => fetchPage(false),
    // After "Not interested" or deleting: take it off the screen without reloading
    remove: (id: string) => (posts.value = posts.value.filter(p => p.id !== id && p.repostOf?.id !== id)),
  }
}
```

```ts
import { api } from '../api'
const following = useFeed(cursor => api.home(cursor))
const forYou = useFeed(cursor => api.forYou(cursor, profile.value))   // profile: a ref, or null for the default
```

## Infinite scroll

Load the next page when a sentinel under the list comes near the viewport. An `IntersectionObserver` with a generous
`rootMargin` starts loading before the member reaches the bottom; a visible *Load more* button keeps it usable for
keyboard and screen-reader users and when observers misbehave.

```vue
<!-- src/components/FeedList.vue -->
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { FabricButton, FabricEmptyState, FabricNotice, FabricSkeleton, FabricStack } from '@feedfabric/fabric-ui'
import PostCard from './PostCard.vue'
import type { FeedPost } from '../types'

const props = defineProps<{
  posts: FeedPost[]; loading: boolean; done: boolean; error: string | null
  feed: 'following' | 'foryou'; empty: string
}>()
const emit = defineEmits<{ more: []; retry: []; remove: [id: string] }>()

const sentinel = ref<HTMLElement>()
let observer: IntersectionObserver | undefined
onMounted(() => {
  observer = new IntersectionObserver(([entry]) => {
    if (entry.isIntersecting && !props.loading && !props.done && !props.error) emit('more')
  }, { rootMargin: '800px 0px' })
  if (sentinel.value) observer.observe(sentinel.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <FabricStack :gap="3" as="section" :aria-busy="loading">
    <PostCard v-for="post in posts" :key="post.id" :post="post" :feed="feed" @remove="emit('remove', $event)" />

    <template v-if="loading && !posts.length">
      <FabricSkeleton v-for="n in 3" :key="n" :lines="3" />
    </template>
    <FabricEmptyState v-else-if="done && !posts.length" :title="empty" icon="sparkle" />
    <FabricNotice v-if="error" tone="bad">
      {{ error }}
      <template #actions><FabricButton size="sm" @click="emit('retry')">Try again</FabricButton></template>
    </FabricNotice>

    <div ref="sentinel" aria-hidden="true" />
    <FabricButton v-if="!done && posts.length && !error" :loading="loading" @click="emit('more')">Load more</FabricButton>
  </FabricStack>
</template>
```

## The home screen: two tabs

```vue
<!-- src/views/HomeView.vue -->
<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { FabricButton, FabricPage, FabricTabs, type TabItem } from '@feedfabric/fabric-ui'
import FeedList from '../components/FeedList.vue'
import { useFeed } from '../composables/useFeed'
import { api } from '../api'

type Tab = 'foryou' | 'following'
const tabs: TabItem<Tab>[] = [{ value: 'foryou', label: 'For you' }, { value: 'following', label: 'Following' }]
const tab = ref<Tab>('foryou')

const forYou = useFeed(cursor => api.forYou(cursor))
const following = useFeed(cursor => api.home(cursor))
const feeds = { foryou: forYou, following }

onMounted(() => forYou.refresh())
// Load a tab the first time it's opened; after that, keep its place
watch(tab, t => !feeds[t].posts.value.length && feeds[t].refresh())
</script>

<template>
  <FabricPage title="Home">
    <template #actions>
      <FabricButton icon="repeat" :loading="feeds[tab].loading.value" @click="feeds[tab].refresh()">Refresh</FabricButton>
    </template>
    <FabricTabs v-model="tab" :tabs="tabs" label="Feeds">
      <template #panel="{ value }">
        <FeedList v-if="value === 'foryou'" feed="foryou" empty="Nothing for you yet. Follow some people or like a few posts."
          :posts="forYou.posts.value" :loading="forYou.loading.value" :done="forYou.done.value" :error="forYou.error.value"
          @more="forYou.more()" @retry="forYou.more()" @remove="forYou.remove" />
        <FeedList v-else feed="following" empty="Posts from people you follow appear here."
          :posts="following.posts.value" :loading="following.loading.value" :done="following.done.value" :error="following.error.value"
          @more="following.more()" @retry="following.more()" @remove="following.remove" />
      </template>
    </FabricTabs>
  </FabricPage>
</template>
```

## Refreshing

- **For You:** don't refresh on a timer. Every first page ranks again, which would reshuffle what the member is
  reading; refresh when they ask (a button, pull to refresh) or when they come back after a while (say, the tab was
  hidden for over 15 minutes, by which time the session has expired anyway).
- **Following:** it's safe to poll the first page every minute or so. Don't push new posts in above the reader:
  hold them behind a *Show N new posts* button and prepend them when it's pressed, swapping in fresh copies of posts
  already on screen.

```ts
// Following: new posts wait behind a button instead of moving the list
const waiting = shallowRef<FeedPost[]>([])
async function poll() {
  const page = await api.home(null)
  const onScreen = new Set(following.posts.value.map(p => p.id))
  waiting.value = page.posts.filter(p => !onScreen.has(p.id))
}
function showNew() {
  following.posts.value = [...waiting.value, ...following.posts.value]
  waiting.value = []
}
```

Polling the first page of Following is cheap and every page is logged to the feed log as served (only once per post
per 30 minutes), so poll at a human pace, not every second.

## Choosing a ranking profile

`GET /userfeed/foryou?profile=<key>` serves For You with any [ranking profile](/how-it-works/profiles/): the six
presets (`balanced`, `community`, `discovery`, `following-first`, `fresh`, `newest`) or a saved one. A few of them make
good extra tabs ("Discover", "Latest"). Each profile has its own session, so switching tabs never mixes orders. Keep
the key in the composable's loader:

```ts
const profile = ref<string | null>(null)   // null: the server's default (Ranking:DefaultProfile)
const forYou = useFeed(cursor => api.forYou(cursor, profile.value))
watch(profile, () => forYou.refresh())
```

An unknown key is `404 profile.not_found` with `params.profile`.

## Other lists

`api.favouritesFeed`, `api.tagPosts` and `api.mentions` page exactly like Following (`nextCursor` is the last item's
ID); `useFeed` works for the first two as is. Mentions return `{ mentions, nextCursor }` items that point at a
`postId` (and a `commentId` when the mention is in a comment) rather than full posts.
