<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { api } from '../api'
import { ago, label } from '../format'
import { changed } from '../session'
import type { Decision, ModerationOverview, ReviewItem, ReviewReason } from '../types'

// The review queue: held posts and comments, oldest first. Approve shows it to everyone; remove hides it from
// everyone, its author included. Each decision becomes a labelled example for retraining the classifier.
const items = ref<ReviewItem[] | null>(null)
const overview = ref<ModerationOverview | null>(null)
const error = ref<string | null>(null)
const deciding = ref(new Set<string>())
const now = ref(Date.now())
const key = (item: ReviewItem) => item.commentId ?? item.postId

async function load() {
  try {
    const [queue, stats] = await Promise.all([api.queue(), api.overview()])
    // Anything decided here stays gone, even if a poll that started before the decision comes back with it
    items.value = queue.filter(i => !deciding.value.has(key(i)))
    overview.value = stats
    now.value = Date.now()
    error.value = null
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function decide(item: ReviewItem, decision: Decision) {
  deciding.value = new Set(deciding.value).add(key(item))
  items.value = items.value?.filter(i => key(i) !== key(item)) ?? null
  try {
    await api.decide(item, decision)
    error.value = null
    changed()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    await load()
    deciding.value.delete(key(item))
  }
}

let timer: number | undefined
onMounted(() => {
  load()
  timer = window.setInterval(load, 10000)
})
onUnmounted(() => window.clearInterval(timer))

const stats = computed(() => overview.value && [
  { label: 'Waiting', value: overview.value.waiting, tone: overview.value.waiting ? 'warn' : 'good' },
  { label: 'Held today', value: overview.value.heldToday },
  { label: 'Blocked today', value: overview.value.blockedToday },
  { label: 'Reports today', value: overview.value.reportsToday },
  { label: 'Reviewed today', value: overview.value.reviewedToday },
  { label: 'Removed today', value: overview.value.removedToday },
])

// "held · insult 0.97", or "reported by 3 people"
function reasonText(r: ReviewReason) {
  return r.action === 'reported' ? `reported by ${r.score} people` : `${r.action} · ${label(r.category)} ${r.score.toFixed(2)}`
}

function reasonTone(action: string) {
  return action === 'reported' ? 'warn' : 'bad'
}

</script>

<template>
  <div class="page-head">
    <h1>Review queue</h1>
    <span v-if="overview?.oldestWaitingSince" class="muted">Oldest waiting since {{ ago(overview.oldestWaitingSince, now) }}</span>
  </div>

  <section v-if="stats" class="stats" aria-label="The last day">
    <div v-for="s in stats" :key="s.label" class="stat card">
      <span class="muted">{{ s.label }}</span>
      <strong :class="s.tone">{{ s.value }}</strong>
    </div>
  </section>

  <p v-if="error" class="error card">{{ error }}</p>

  <p v-if="items && !items.length" class="card muted">Nothing waiting. Held posts and comments, and ones members have reported, show up here.</p>
  <p v-else-if="!items && !error" class="card muted">Loading…</p>

  <article v-for="item in items ?? []" :key="key(item)" class="item card">
    <div class="meta">
      <span class="chip">{{ item.kind }}</span>
      <RouterLink :to="{ name: 'user', params: { externalUserId: item.author.externalUserId } }" class="who">{{ item.author.username }}</RouterLink>
      <span class="muted">{{ item.author.externalUserId }} · posted {{ ago(item.createdAt, now) }} · held {{ ago(item.heldAt, now) }}</span>
    </div>
    <p class="content">{{ item.content }}</p>
    <div class="why">
      <span v-for="(r, i) in item.reasons" :key="i" class="chip" :class="reasonTone(r.action)">{{ reasonText(r) }}</span>
      <span v-for="(count, reason) in item.reports" :key="reason" class="chip warn">{{ label(String(reason)) }} ×{{ count }}</span>
    </div>
    <div class="actions">
      <button class="approve" :disabled="deciding.has(key(item))" @click="decide(item, 'approved')">Approve<span class="sr-only">, show it to everyone</span></button>
      <button class="remove" :disabled="deciding.has(key(item))" @click="decide(item, 'removed')">Remove<span class="sr-only">, hide it from everyone</span></button>
      <RouterLink :to="{ name: 'post', params: { postId: item.postId } }" class="link">Open the {{ item.kind === 'post' ? 'post' : 'post it’s on' }}</RouterLink>
    </div>
  </article>
</template>

<style scoped>
.stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 8px; }
.stat { display: flex; flex-direction: column; padding: 10px 14px; }
.stat strong { font-size: 22px; font-variant-numeric: tabular-nums; }
.stat strong.warn { color: var(--warn); }
.stat strong.good { color: var(--good); }
.item { display: flex; flex-direction: column; gap: 8px; }
.meta { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 8px; }
.content { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 16px; }
.why { display: flex; flex-wrap: wrap; gap: 6px; }
.actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.who { font-weight: 700; }
.approve:hover:not(:disabled) { border-color: var(--good); color: var(--good); background: var(--good-soft); }
.remove:hover:not(:disabled) { border-color: var(--bad); color: var(--bad); background: var(--bad-soft); }
</style>
