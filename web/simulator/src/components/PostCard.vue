<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, describeError } from '../api'
import { useImpression } from '../composables/useImpressions'
import { ago } from '../format'
import { changed } from '../session'
import type { CardPost } from '../types'
import Avatar from './Avatar.vue'
import CommentThread from './CommentThread.vue'
import PostImages from './PostImages.vue'
import RichText from './RichText.vue'
import WhyThisPost from './WhyThisPost.vue'

// One post as a row of a feed, as on X: the author's avatar top left, who and when, the text, photos and a footer
const props = defineProps<{ post: CardPost; now: number; showAuthor?: boolean; threadOpen?: boolean; fresh?: boolean }>()
const router = useRouter()
// A repost shows whose post it is, even on the reposter's own profile
const withAuthor = computed(() => !!props.post.author && (props.showAuthor || !!props.post.repostedBy))

// Optimistic like: shown at once, replaced by the server's numbers once the request settles and the next poll lands
const pending = ref<{ liked: boolean; likes: number } | null>(null)
const liked = computed(() => pending.value?.liked ?? props.post.likedByViewer)
const likes = computed(() => pending.value?.likes ?? props.post.likes)
const popping = ref(false)
// Optimistic repost, the same way
const pendingRepost = ref<{ reposted: boolean; reposts: number } | null>(null)
const reposted = computed(() => pendingRepost.value?.reposted ?? props.post.repostedByViewer)
const reposts = computed(() => pendingRepost.value?.reposts ?? props.post.reposts)
const error = ref<string | null>(null)
const open = ref(props.threadOpen ?? false)
// Hashtags are links in the text already; inferred topics get their own chips
const topics = computed(() => props.post.tags.filter(t => t.source === 'inferred'))
const row = ref<HTMLElement>()
const hidden = ref(false)

// For You only: hides the post now, and the ranker shows less like it from here on
async function notInterested() {
  hidden.value = true
  try {
    await api.notInterested(props.post.postId)
  } catch (e) {
    hidden.value = false
    error.value = describeError(e)
  }
}
useImpression(row, () => props.post.postId)

// Clicking anywhere on a post opens it, unless the click was on one of its own controls or links, or ended a
// text selection. The timestamp is the same link for keyboard and screen reader users.
function openPost(event: MouseEvent) {
  if (props.threadOpen) return
  if ((event.target as HTMLElement).closest('a, button, input, textarea, select, label, form')) return
  if (window.getSelection()?.toString()) return
  router.push({ name: 'post', params: { postId: props.post.postId } })
}

async function toggleLike() {
  const next = !liked.value
  pending.value = { liked: next, likes: likes.value + (next ? 1 : -1) }
  popping.value = false
  requestAnimationFrame(() => (popping.value = true))
  try {
    await (next ? api.like(props.post.postId) : api.unlike(props.post.postId))
    error.value = null
    changed()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    // Hold the optimistic value briefly so the old count doesn't flash back before the refetch
    setTimeout(() => (pending.value = null), 1500)
  }
}

async function toggleRepost() {
  const next = !reposted.value
  pendingRepost.value = { reposted: next, reposts: reposts.value + (next ? 1 : -1) }
  try {
    await (next ? api.repost(props.post.postId) : api.unrepost(props.post.postId))
    error.value = null
    changed()
  } catch (e) {
    error.value = describeError(e)
  } finally {
    setTimeout(() => (pendingRepost.value = null), 1500)
  }
}
</script>

<template>
  <p v-if="hidden" class="post hidden muted">Hidden. You'll see less like this from {{ post.author?.username ?? 'this author' }} and on its topics.</p>
  <article v-else ref="row" class="post" :class="{ fresh, clickable: !threadOpen, reposted: post.repostedBy }" @click="openPost">
    <p v-if="post.repostedBy" class="repost-line muted">
      <span aria-hidden="true">↻</span>
      <RouterLink :to="{ name: 'profile', params: { externalUserId: post.repostedBy.externalUserId } }">{{ post.repostedBy.username }}</RouterLink> reposted
    </p>
    <Avatar v-if="withAuthor" :name="post.author!.username" />
    <div class="body">
      <div class="head">
        <RouterLink v-if="withAuthor" class="who" :to="{ name: 'profile', params: { externalUserId: post.author!.externalUserId } }">
          {{ post.author!.username }}
        </RouterLink>
        <RouterLink :to="{ name: 'post', params: { postId: post.postId } }" class="muted">{{ ago(post.createdAt, now) }}</RouterLink>
      </div>
      <RichText :text="post.content" />
      <PostImages v-if="post.media?.length" :media="post.media" />
      <div class="foot">
        <div class="actions">
          <button class="plain pill heart" :class="{ on: liked, pop: popping }" :aria-pressed="liked" @click="toggleLike">
            {{ liked ? '♥' : '♡' }} {{ likes }}<span class="sr-only"> likes</span>
          </button>
          <button class="plain pill talk" :class="{ on: open }" :aria-expanded="open" @click="open = !open">
            {{ post.comments }} {{ post.comments === 1 ? 'comment' : 'comments' }}
          </button>
          <button class="plain pill boost" :class="{ on: reposted }" :aria-pressed="reposted" @click="toggleRepost">
            ↻ {{ reposts }}<span class="sr-only"> {{ reposted ? 'reposts, undo your repost' : 'reposts, repost' }}</span>
          </button>
          <button v-if="post.ranking" class="plain pill" @click="notInterested">Not interested</button>
        </div>
        <div v-if="topics.length" class="topics">
          <RouterLink v-for="t in topics" :key="t.tag" :to="{ name: 'tag', params: { tag: t.tag } }" title="Topic matched from the wording">{{ t.tag }}</RouterLink>
        </div>
      </div>
      <WhyThisPost v-if="post.ranking" :ranking="post.ranking" />
      <p v-if="error" class="error">{{ error }}</p>
      <CommentThread v-if="open" :post-id="post.postId" />
    </div>
  </article>
</template>

<style scoped>
.post { display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; }
.post.reposted { flex-wrap: wrap; row-gap: 4px; }
.repost-line { flex-basis: 100%; margin: 0; font-size: 12.5px; }
.repost-line a { color: inherit; font-weight: 600; }
.post.clickable { cursor: pointer; }
.post.clickable:hover { background: color-mix(in srgb, var(--ink) 3%, transparent); }
.post.fresh { animation: flash 1.6s ease-out; }
.post.hidden { margin: 0; font-size: 13px; }
.body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; }
.head { display: flex; gap: 8px; align-items: baseline; font-size: 13px; }
.who { font-weight: 650; font-size: 14px; }
.foot { display: flex; justify-content: space-between; align-items: center; gap: 8px; flex-wrap: wrap; }
.actions { display: flex; gap: 4px; }
.pill { padding: 2px 10px; border-radius: 99px; color: var(--muted); font-size: 12.5px; }
.heart:hover, .heart.on { color: var(--like); }
.heart:hover { background: var(--like-soft); }
.heart.pop { animation: pop .35s ease-out; }
.talk:hover, .talk.on { background: var(--me-soft); color: var(--me); }
.boost.on { color: var(--accent); }
.boost:hover { background: var(--accent-soft); color: var(--accent); }
.topics { display: flex; flex-wrap: wrap; gap: 4px; }
.topics a { font-size: 11.5px; padding: 0 8px; border-radius: 99px; border: 1px solid var(--line); color: var(--muted); }
.topics a:hover { border-color: var(--accent); color: var(--accent); }
.error { margin: 0; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
:deep(p) { margin: 0; }
</style>
