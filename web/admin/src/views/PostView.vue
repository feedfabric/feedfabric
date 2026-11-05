<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import LogList from '../components/LogList.vue'
import ReportList from '../components/ReportList.vue'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { ago, fmt, statusTone } from '../format'
import { changed } from '../session'
import type { AdminComment, AdminPostDetail, Decision } from '../types'

// One post as staff see it, whatever its state: its comments (held and removed ones included), reports and
// moderation history. Approve shows a post or comment to everyone; remove hides it from everyone.
const props = defineProps<{ postId: string }>()
const detail = ref<AdminPostDetail | null>(null)
const missing = ref(false)
const busy = ref(new Set<string>())
const actionError = ref<string | null>(null)
const now = useNow()

const { error } = usePolling(async () => {
  try {
    detail.value = await api.post(props.postId)
    missing.value = false
  } catch (e) {
    if ((e as { status?: number }).status === 404) {
      missing.value = true
      return
    }
    throw e
  }
}, { intervalMs: 15000 })

const post = computed(() => detail.value?.post)
// Replies sit under the comment they answer
const depth = (c: AdminComment) => (c.parentCommentId ? 1 : 0)
const threaded = computed(() => {
  const comments = detail.value?.comments ?? []
  const top = comments.filter(c => !c.parentCommentId)
  return top.flatMap(c => [c, ...comments.filter(r => r.parentCommentId === c.id)])
})

async function decide(target: { postId: string; commentId: string | null }, decision: Decision) {
  const key = target.commentId ?? target.postId
  busy.value = new Set(busy.value).add(key)
  try {
    await api.decide(target, decision)
    actionError.value = null
    changed()
  } catch (e) {
    actionError.value = e instanceof Error ? e.message : String(e)
  } finally {
    const next = new Set(busy.value)
    next.delete(key)
    busy.value = next
  }
}
</script>

<template>
  <p v-if="missing" class="card muted">No such post. It may have been a repost, which has no page of its own.</p>
  <template v-else>
    <div class="page-head">
      <h1>Post</h1>
      <div v-if="post" class="actions">
        <span class="chip" :class="statusTone(post.status)">{{ post.status }}</span>
        <template v-if="post.status !== 'deleted'">
          <button v-if="post.status !== 'visible'" class="good" :disabled="busy.has(post.id)" @click="decide({ postId: post.id, commentId: null }, 'approved')">
            Approve
          </button>
          <button v-if="post.status !== 'removed'" class="danger" :disabled="busy.has(post.id)" @click="decide({ postId: post.id, commentId: null }, 'removed')">
            Remove
          </button>
        </template>
      </div>
    </div>
    <p v-if="error || actionError" class="error card">{{ error || actionError }}</p>

    <article v-if="post" class="card post">
      <div class="meta">
        <RouterLink :to="{ name: 'user', params: { externalUserId: post.author.externalUserId } }" class="who">{{ post.author.username }}</RouterLink>
        <span class="muted">posted {{ ago(post.createdAt, now) }}<template v-if="post.editedAt"> · edited {{ ago(post.editedAt, now) }}</template></span>
      </div>
      <p class="content-text body">{{ post.content }}</p>
      <div v-if="detail?.media.length" class="media">
        <a v-for="m in detail.media" :key="m.id" :href="m.url" target="_blank" rel="noopener"><img :src="m.thumbnailUrl" :alt="`Photo ${m.width}×${m.height}`"></a>
      </div>
      <div class="foot">
        <span v-for="t in detail?.tags ?? []" :key="t.tag" class="chip plain">{{ t.source === 'hashtag' ? '#' : '' }}{{ t.tag }}</span>
        <span class="counts muted">{{ fmt(post.likes) }} likes · {{ fmt(post.comments) }} comments · {{ fmt(post.reposts) }} reposts · {{ fmt(post.reports) }} reports</span>
      </div>
    </article>
    <p v-else-if="!error" class="card"><span class="skeleton" /></p>

    <div v-if="detail" class="grid two">
      <section class="card flush wide">
        <h2>Comments</h2>
        <ul class="rows">
          <li v-for="c in threaded" :key="c.id" class="comment" :style="{ paddingLeft: `${16 + depth(c) * 24}px` }">
            <div class="ctext">
              <RouterLink :to="{ name: 'user', params: { externalUserId: c.author.externalUserId } }" class="who">{{ c.author.username }}</RouterLink>
              <span class="muted"> · {{ ago(c.createdAt, now) }}</span>
              <span v-if="c.reports" class="chip warn">{{ c.reports }} {{ c.reports === 1 ? 'report' : 'reports' }}</span>
              <p class="content-text">{{ c.content }}</p>
            </div>
            <div class="cactions">
              <span v-if="c.status !== 'visible'" class="chip" :class="statusTone(c.status)">{{ c.status }}</span>
              <template v-if="c.status !== 'deleted'">
                <button v-if="c.status !== 'visible'" class="good" :disabled="busy.has(c.id)" @click="decide({ postId: c.postId, commentId: c.id }, 'approved')">Approve</button>
                <button v-if="c.status !== 'removed'" class="danger" :disabled="busy.has(c.id)" @click="decide({ postId: c.postId, commentId: c.id }, 'removed')">Remove</button>
              </template>
            </div>
          </li>
          <li v-if="!threaded.length" class="muted">No comments.</li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Reports</h2>
        <ReportList :reports="detail.reports" :now="now" />
      </section>
      <section class="card flush">
        <h2>Moderation history</h2>
        <LogList :entries="detail.history" :now="now" show-author />
      </section>
    </div>
  </template>
</template>

<style scoped>
.actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.post { display: flex; flex-direction: column; gap: 10px; }
.meta { display: flex; flex-wrap: wrap; gap: 4px 8px; align-items: baseline; }
.who { font-weight: 600; }
.body { font-size: 17px; }
.media { display: flex; flex-wrap: wrap; gap: 8px; }
.media img { width: 140px; height: 140px; object-fit: cover; border-radius: 8px; border: 1px solid var(--line); display: block; }
.foot { display: flex; flex-wrap: wrap; gap: 6px; align-items: baseline; }
.counts { margin-left: auto; }
.wide { grid-column: 1 / -1; }
.comment { align-items: flex-start; }
.ctext { min-width: 0; }
.ctext p { margin: 2px 0 0; }
.cactions { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; flex: none; }
.cactions button { padding: 3px 10px; font-size: 13px; }
</style>
