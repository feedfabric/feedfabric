<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { ago } from '../format'
import { session } from '../session'
import type { Activity, Engagement } from '../types'

const engagement = ref<Engagement | null>(null)
const now = useNow()
const seen = new Set<string>()
const fresh = ref(new Set<string>())
let first = true

const key = (a: Activity) => `${a.kind}:${a.actorExternalUserId}:${a.postId}:${a.createdAt}`
const verbs = { like: 'liked', comment: 'commented on', reply: 'replied on', repost: 'reposted' } as const

const { error } = usePolling(async () => {
  const next = await api.engagement()
  fresh.value = first ? new Set() : new Set(next.recent.map(key).filter(k => !seen.has(k)))
  next.recent.forEach(a => seen.add(key(a)))
  first = false
  engagement.value = next
}, { watch: [() => session.actingAs] })

const maxScore = computed(() => Math.max(1, ...(engagement.value?.affinity.map(a => a.score) ?? [])))
</script>

<template>
  <div class="card">
    <h2>Live activity</h2>
    <p v-if="error" class="error">{{ error }}</p>
    <div class="activity">
      <div v-for="a in engagement?.recent ?? []" :key="key(a)" class="event" :class="{ fresh: fresh.has(key(a)) }">
        <RouterLink class="who" :to="{ name: 'profile', params: { externalUserId: a.actorExternalUserId } }">{{ a.actorUsername }}</RouterLink>
        {{ verbs[a.kind] }}
        <RouterLink class="who" :to="{ name: 'profile', params: { externalUserId: a.authorExternalUserId } }">{{ a.authorUsername }}</RouterLink>'s
        <RouterLink :to="{ name: 'post', params: { postId: a.postId } }">post</RouterLink>
        <span class="muted"> · {{ ago(a.createdAt, now) }}</span>
        <span v-if="a.commentContent" class="said">“{{ a.commentContent }}”</span>
        <RouterLink class="snippet" :to="{ name: 'post', params: { postId: a.postId } }">{{ a.postContent }}</RouterLink>
      </div>
      <p v-if="engagement && !engagement.recent.length" class="empty">Nothing yet. Like or comment on a post, or run the simulator.</p>
    </div>

    <h2>{{ session.actingAsName }}'s top topics</h2>
    <div class="topics">
      <RouterLink v-for="t in engagement?.topics ?? []" :key="t.tag" class="topic" :to="{ name: 'tag', params: { tag: t.tag } }">
        {{ t.tag }} <span class="num">{{ t.score }}</span>
      </RouterLink>
      <p v-if="engagement && !engagement.topics.length" class="empty">Nothing yet. Topics build up from what {{ session.actingAsName }} likes and comments on.</p>
    </div>

    <h2>{{ session.actingAsName }}'s top authors</h2>
    <div class="bars">
      <div v-for="a in engagement?.affinity ?? []" :key="a.author.externalUserId" class="bar-row">
        <RouterLink :to="{ name: 'profile', params: { externalUserId: a.author.externalUserId } }">{{ a.author.username }}</RouterLink>
        <div class="track"><div class="fill" :style="{ width: `${(a.score / maxScore) * 100}%` }" /></div>
        <span class="num">{{ a.score }}</span>
      </div>
      <p v-if="engagement && !engagement.affinity.length" class="empty">Nothing yet. Scores appear once the worker processes likes and comments.</p>
      <p v-else class="muted">Scored in Redis by the worker: a like counts 1, a comment 2.</p>
    </div>
  </div>
</template>

<style scoped>
.topics { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 18px; }
.topic { font-size: 12.5px; padding: 2px 10px; border-radius: 99px; background: var(--me-soft); color: var(--me); }
.activity { display: flex; flex-direction: column; gap: 8px; margin-bottom: 18px; max-height: 420px; overflow: auto; }
.event { font-size: 13px; line-height: 1.4; padding-left: 10px; border-left: 2px solid var(--like-soft); }
.event.fresh { border-left-color: var(--like); animation: flash 1.6s ease-out; }
.who { font-weight: 600; }
.said { display: block; font-style: italic; }
.snippet { display: block; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.bars { display: flex; flex-direction: column; gap: 6px; }
.bar-row { display: grid; grid-template-columns: 110px 1fr 28px; gap: 8px; align-items: center; font-size: 13px; }
.bar-row a { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.track { height: 8px; border-radius: 99px; background: var(--like-soft); overflow: hidden; }
.fill { height: 100%; background: var(--like); border-radius: 99px; transition: width .4s; }
.num { text-align: right; color: var(--muted); font-variant-numeric: tabular-nums; }
p { margin: 4px 0 0; }
@media (max-width: 760px) { .card { position: static; } }
</style>
