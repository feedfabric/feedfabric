---
layout: ../../layouts/Doc.astro
title: Post cards and actions
description: Rendering a feed post (reposts, held posts, tags, photos), the actions on it, a comments thread, and the "Why this post?" panel built from ranking.factors.
---

## Anatomy of a feed post

| Field | Render it as |
|---|---|
| `repostOf` (not null) | A repost. Show "Reposted by @`authorUsername`" above the **original** (`repostOf`), and use the original for everything else on the card. |
| `authorUsername`, `authorExternalUserId` | The byline. `@username` is public and @mention-able; `externalUserId` is what follow and favourite endpoints take. |
| `content` | Text with `#tags` and `@mentions` linked (same patterns as the server, below). Keep line breaks (`white-space: pre-line`). |
| `createdAt`, `editedAt` | Relative time; "edited" when `editedAt` is set. |
| `isHeld` | Only ever true on the member's **own** post. Say that only they can see it while it's reviewed. |
| `tags` | Chips linking to the tag feed. `source` is `hashtag` or `inferred`; you might show only hashtags in the text and inferred topics as subtle chips. |
| `media` | Up to four photos. Show `thumbnailUrl` in the card and `url` when opened; `width`/`height` give the aspect ratio before they load. URLs expire after an hour. |
| `ranking` (For You only) | *Why this post?*, and an "exploring" note when `explored` is true. |
| `likes`, `comments`, `reposts`, `liked`, `reposted` | Counts next to the actions, and the Like and Repost buttons pressed when the member has. On a repost, read them from `repostOf`. |

Feed posts say how many likes, comments and reposts they have, and whether the member has liked or reposted them.
The same post can be in a feed twice, as itself and as what someone's repost shares, so keep the member's changes in
one place by post ID and read from there, falling back to what the feed said.

## Engagement state

```ts
// src/stores/engagement.ts: the member's likes and reposts since the feed loaded, by post ID, over what the feed
// said. Shown at once and put back if the API says no; PUT/DELETE are idempotent, so racing another tab never
// doubles anything.
import { reactive } from 'vue'
import { api } from '../api'
import type { FeedPost } from '../types'

interface State { on: boolean; count: number }
const likes = reactive(new Map<string, State>())
const reposts = reactive(new Map<string, State>())

const read = (map: Map<string, State>, post: FeedPost, on: boolean, count: number) => map.get(post.id) ?? { on, count }

async function toggle(map: Map<string, State>, post: FeedPost, was: State, on: () => Promise<void>, off: () => Promise<void>) {
  map.set(post.id, { on: !was.on, count: was.count + (was.on ? -1 : 1) })
  try {
    await (was.on ? off() : on())
  } catch (e) {
    map.set(post.id, was)   // put it back
    throw e
  }
}

export const engagement = {
  like: (post: FeedPost) => read(likes, post, post.liked, post.likes),
  repost: (post: FeedPost) => read(reposts, post, post.reposted, post.reposts),
  toggleLike: (post: FeedPost) => toggle(likes, post, engagement.like(post), () => api.like(post.id), () => api.unlike(post.id)),
  toggleRepost: (post: FeedPost) =>
    toggle(reposts, post, engagement.repost(post), () => api.repost(post.id), () => api.unrepost(post.id)),
}
```

## Linking tags and mentions

The server's patterns (`TextTokens.cs`): a hashtag starts with a letter (`#5k` isn't one), a mention is a username
after `@`, and neither may follow a letter, digit or another `#`/`@`.

```vue
<!-- src/components/RichText.vue -->
<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ text: string }>()
const pattern = /(?<![\w#@])(#[A-Za-z][A-Za-z0-9_]{1,49}|@[A-Za-z0-9][A-Za-z0-9_-]{0,49})/g

// split() with a capturing group alternates plain text (even indexes) and matches (odd ones). Rendered as text nodes
// and links, never as HTML.
const parts = computed(() => props.text.split(pattern).map((part, i) =>
  i % 2 === 0 ? { text: part }
  : part.startsWith('#') ? { tag: part.slice(1).toLowerCase(), text: part }
  : { user: part.slice(1).toLowerCase(), text: part }))
</script>

<template>
  <p class="text">
    <template v-for="(part, i) in parts" :key="i">
      <RouterLink v-if="'tag' in part" :to="`/tags/${part.tag}`">{{ part.text }}</RouterLink>
      <RouterLink v-else-if="'user' in part" :to="`/@${part.user}`">{{ part.text }}</RouterLink>
      <template v-else>{{ part.text }}</template>
    </template>
  </p>
</template>

<style scoped>
.text { margin: 0; white-space: pre-line; overflow-wrap: anywhere; }
</style>
```

## The card

```vue
<!-- src/components/PostCard.vue -->
<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  FabricAvatar, FabricBadge, FabricButton, FabricCard, FabricChip, FabricIcon, FabricMenu, FabricStack, useToast, type MenuAction,
} from '@feedfabric/fabric-ui'
import RichText from './RichText.vue'
import PostPhotos from './PostPhotos.vue'
import ReportDialog from './ReportDialog.vue'
import WhyThisPost from './WhyThisPost.vue'
import { timeAgo } from '../format'
import { useImpression } from '../composables/useImpression'
import { engagement } from '../stores/engagement'
import { api } from '../api'
import { describe } from '../api/errors'
import type { FeedPost } from '../types'

const props = defineProps<{ post: FeedPost; feed: 'following' | 'foryou' | 'other' }>()
const emit = defineEmits<{ remove: [id: string] }>()
const { toast } = useToast()

// A repost shows, and is acted on as, the post it shares
const shown = computed(() => props.post.repostOf ?? props.post)
const repostedBy = computed(() => (props.post.repostOf ? props.post.authorUsername : null))

// Reports an impression and dwell for the post the member actually sees (see "Reading signals")
const root = ref<InstanceType<typeof FabricCard>>()
useImpression(root, () => shown.value.id)

const like = computed(() => engagement.like(shown.value))
const repost = computed(() => engagement.repost(shown.value))
const run = (action: () => Promise<unknown>) => action().catch(e => toast({ title: describe(e), tone: 'bad' }))
const reportOpen = ref(false)

const menu = computed<MenuAction[]>(() => [
  ...(props.feed === 'foryou'
    ? [
        { label: 'Not interested', icon: 'eye-off' as const, onSelect: () => run(async () => {
          await api.notInterested(shown.value.id)
          emit('remove', props.post.id)
          toast({ title: "Got it. You'll see less like this." })
        }) },
        { label: 'Show more like this', icon: 'sparkle' as const, onSelect: () => run(async () => {
          await api.showMore(shown.value.id)
          toast({ title: "Got it. You'll see more like this.", tone: 'good' })
        }) },
      ]
    : []),
  { label: `Follow @${shown.value.authorUsername}`, icon: 'user' as const, onSelect: () => run(() => api.follow(shown.value.authorExternalUserId)) },
  { label: 'Add to favourites', icon: 'star' as const, onSelect: () => run(() => api.addFavourite(shown.value.authorExternalUserId)) },
  { label: 'Report', icon: 'flag' as const, tone: 'danger' as const, onSelect: () => { reportOpen.value = true } },
])
</script>

<template>
  <FabricCard ref="root" as="article" :aria-labelledby="`post-${post.id}-by`">
    <p v-if="repostedBy" class="context"><FabricIcon name="repeat" :size="14" /> Reposted by @{{ repostedBy }}</p>

    <header class="byline">
      <FabricAvatar :name="shown.authorUsername" decorative />
      <RouterLink :id="`post-${post.id}-by`" :to="`/@${shown.authorUsername}`">@{{ shown.authorUsername }}</RouterLink>
      <time :datetime="shown.createdAt">{{ timeAgo(shown.createdAt) }}</time>
      <span v-if="shown.editedAt" class="muted">· edited</span>
      <FabricMenu :items="menu" label="More actions on this post" class="more" />
    </header>

    <FabricBadge v-if="shown.isHeld" tone="warn">Only you can see this while it's reviewed</FabricBadge>
    <RichText :text="shown.content" />
    <PostPhotos v-if="shown.media.length" :photos="shown.media" :author="shown.authorUsername" />

    <FabricStack v-if="shown.tags.some(t => t.source === 'inferred')" direction="row" wrap :gap="1">
      <FabricChip v-for="t in shown.tags.filter(t => t.source === 'inferred')" :key="t.tag">{{ t.tag }}</FabricChip>
    </FabricStack>

    <FabricStack direction="row" :gap="1" class="actions">
      <FabricButton variant="ghost" size="sm" icon="heart" :aria-pressed="like.on" @click="run(() => engagement.toggleLike(shown))">
        {{ like.on ? 'Liked' : 'Like' }} · {{ like.count }}
      </FabricButton>
      <FabricButton variant="ghost" size="sm" icon="message" :to="{ name: 'post', params: { id: shown.id } }">
        Comment · {{ shown.comments }}
      </FabricButton>
      <FabricButton variant="ghost" size="sm" icon="repeat" :aria-pressed="repost.on" :disabled="shown.isHeld"
        @click="run(() => engagement.toggleRepost(shown))">{{ repost.on ? 'Reposted' : 'Repost' }} · {{ repost.count }}</FabricButton>
    </FabricStack>

    <WhyThisPost v-if="post.ranking" :ranking="post.ranking" />
    <ReportDialog v-model:open="reportOpen" :post-id="shown.id" />
  </FabricCard>
</template>
```

`timeAgo` is yours (a few lines of `Intl.RelativeTimeFormat`), as is `ReportDialog`: a `FabricDialog` with the seven
reasons (`spam`, `harassment`, `hate`, `threat`, `sexual`, `self_harm`, `other`) that calls `api.reportPost`. Note
`ranking` is read from the **feed item** (`post.ranking`), since For You never serves reposts; everything else from
`shown`.

## Photos

```vue
<!-- src/components/PostPhotos.vue -->
<script setup lang="ts">
import type { PostMedia } from '../types'
defineProps<{ photos: PostMedia[]; author: string }>()
</script>

<template>
  <div class="photos" :class="`n${photos.length}`">
    <a v-for="(photo, i) in photos" :key="photo.id" :href="photo.url" target="_blank" rel="noopener">
      <img :src="photo.thumbnailUrl" :width="photo.width" :height="photo.height" loading="lazy" decoding="async"
        :alt="`Photo ${i + 1} of ${photos.length} from @${author}`" />
    </a>
  </div>
</template>

<style scoped>
.photos { display: grid; gap: 4px; border-radius: var(--fabric-radius-md); overflow: hidden; }
.photos.n2, .photos.n4 { grid-template-columns: 1fr 1fr; }
.photos.n3 { grid-template-columns: 2fr 1fr; }
.photos.n3 a:first-child { grid-row: span 2; }
img { display: block; width: 100%; height: 100%; max-height: 520px; object-fit: cover; background: var(--fabric-sunken); }
.photos.n1 img { height: auto; aspect-ratio: auto; }
</style>
```

Posts carry no alt text, so describe the photo by position and author rather than leaving `alt` empty.

## Comments

A post page renders the post you navigated from (there's no `GET` for one post), reports an open, and loads its
thread:

```ts
// src/views/PostView.vue (script): `post` comes from the store or router state, not a fetch
onMounted(() => {
  api.open(post.id).catch(() => {})        // the member opened the post: a ranking signal
  comments.refresh()
})
const comments = usePaged(cursor => api.comments(post.id, cursor))   // like useFeed, over { comments, nextCursor }

async function reply(content: string, parentCommentId: string | null) {
  const created = await api.comment(post.id, content, parentCommentId)   // 201 with the comment
  // Show it at once; if created.isHeld, label it "Only you can see this while it's reviewed"
}
```

- Top-level comments come **oldest first**; each has a `replyCount`, and its replies load on demand with
  `api.replies(commentId)`.
- A comment with `isDeleted: true` has `content: null`: show "Comment deleted" so the replies under it still make
  sense.
- `content.violation` comes back exactly as for posts; keep the text and say which rule.

## Why this post?

Every For You post carries `ranking.factors`: the reasons, in words, each a **multiplier** on the score. `1` means no
effect; above 1 pushed the post up, below 1 pulled it down. The labels you'll see:

| Label | From |
|---|---|
| Recent, Your topics, Similar to what you like, This session, Author you like, You follow them, Popular, Trending now, Holds attention | The built-in formula's parts (shown whichever ranker scored, as the formula's view of the post) |
| Profile formula | The profile's own formula; `value` is its score and `detail` the formula, rather than a multiplier |
| Newest first | The `newest` profile |
| Freshness, Boosted, Lowered, Seen already | Adjustments from the profile and the member's history |

```vue
<!-- src/components/WhyThisPost.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { FabricBadge, FabricDisclosure } from '@feedfabric/fabric-ui'
import type { PostRanking } from '../types'

const props = defineProps<{ ranking: PostRanking }>()
const formula = computed(() => props.ranking.factors.find(f => f.label === 'Profile formula'))
const factors = computed(() => props.ranking.factors.filter(f => f.label !== 'Profile formula'))
const ranker = computed(() => {
  const r = props.ranking.ranker
  if (r.startsWith('model:')) return 'the trained model'
  if (r.startsWith('expression')) return "this feed's own formula"
  return r === 'newest' ? 'newest first' : 'the scoring formula'
})
</script>

<template>
  <FabricDisclosure summary="Why this post?" :hint="`Ranked by ${ranker}`">
    <FabricBadge v-if="ranking.explored" tone="info">Exploring</FabricBadge>
    <p v-if="ranking.explored">Shown in a slot kept for posts the ranker wouldn't have picked, so it can learn what else you like.</p>
    <p v-if="formula"><code>{{ formula.detail }}</code> = {{ formula.value.toFixed(3) }}</p>
    <ul>
      <li v-for="f in factors" :key="f.label + f.detail">
        <strong>{{ f.label }}</strong> ×{{ f.value.toFixed(2) }}
        <span class="detail">{{ f.detail }}</span>
      </li>
    </ul>
  </FabricDisclosure>
</template>
```

Sort or trim the list as suits your design; a short "Because you follow @maya and it's popular in #running" built
from the two or three largest factors reads better on a phone than the full table.

## Accessibility and phone width

- Each card is an `<article>` labelled by its byline, so screen-reader users can move post by post.
- Toggle buttons say their state with `aria-pressed` and a changing label ("Like" / "Liked"); icon-only buttons need
  FabricButton's `label`.
- Infinite scroll keeps a real *Load more* button, and `aria-busy` on the list while a page loads.
- Toasts from `useToast` announce themselves; `tone: 'bad'` ones interrupt, the rest are polite.
- Photos get descriptive `alt` text and fixed dimensions, so the page doesn't jump as they load.
- At phone width keep one column, put secondary actions in the `FabricMenu`, and give touch targets at least 40px.
  `FabricAppShell` folds the sidebar into a menu for you.
- Respect `prefers-reduced-motion`; FabricUI's own motion tokens drop to zero under it.
- Don't count an impression for a post that's covered by a dialog or in a background tab
  ([Reading signals](/build-a-ui/signals/)).
