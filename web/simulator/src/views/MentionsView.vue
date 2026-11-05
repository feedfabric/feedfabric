<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import { useNow } from '../composables/useNow'
import { useOlderPages } from '../composables/useOlderPages'
import { usePolling } from '../composables/usePolling'
import { ago } from '../format'
import { session } from '../session'
import type { Mention } from '../types'
import RichText from '../components/RichText.vue'
import ScrollSentinel from '../components/ScrollSentinel.vue'

const latest = ref<Mention[] | null>(null)
const now = useNow()
const older = useOlderPages<Mention>(async cursor => {
  const page = await api.mentions(cursor)
  return { items: page.mentions, nextCursor: page.nextCursor }
})
watch(() => session.actingAs, () => {
  latest.value = null
  older.reset()
})

const { error } = usePolling(async () => {
  const page = await api.mentions()
  latest.value = page.mentions
  older.follow(page.nextCursor)
}, { intervalMs: 5000, watch: [() => session.actingAs] })

const mentions = computed(() => latest.value && [...latest.value, ...older.items.value.filter(o => !latest.value!.some(m => m.id === o.id))])
</script>

<template>
  <div class="stack">
    <div class="page-head"><h1>Mentions of @{{ session.actingAsName }}</h1></div>
    <p v-if="error" class="error">{{ error }}</p>
    <div class="feed">
      <p v-if="!mentions" class="feed-note"><span class="skeleton" /></p>
      <p v-else-if="!mentions.length" class="feed-note">
        Nobody has mentioned {{ session.actingAsName }} yet. Write @{{ session.actingAsName }} in a post or comment as someone else to try it.
      </p>
      <article v-for="m in mentions ?? []" :key="m.id" class="mention">
        <RouterLink class="who" :to="{ name: 'profile', params: { externalUserId: m.authorExternalUserId } }">{{ m.authorUsername }}</RouterLink>
        <span class="muted"> mentioned you in a {{ m.commentId ? 'comment' : 'post' }} · </span>
        <RouterLink class="muted" :to="{ name: 'post', params: { postId: m.postId } }">{{ ago(m.createdAt, now) }}</RouterLink>
        <RichText :text="m.content" />
      </article>
      <p v-if="older.loading.value" class="feed-note">Loading more…</p>
    </div>
    <ScrollSentinel v-if="mentions?.length" :active="!!older.nextCursor.value" :loading="older.loading.value" @more="older.loadMore" />
  </div>
</template>

<style scoped>
.mention { padding: 12px 16px; }
.who { font-weight: 600; }
</style>
