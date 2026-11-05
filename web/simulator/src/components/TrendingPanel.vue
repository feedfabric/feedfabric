<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import { usePolling } from '../composables/usePolling'
import type { TrendingTag } from '../types'

const tags = ref<TrendingTag[]>([])
const { error } = usePolling(async () => {
  tags.value = await api.trending()
}, { intervalMs: 10000 })
</script>

<template>
  <div class="card">
    <h2>Trending today</h2>
    <p v-if="error" class="error">{{ error }}</p>
    <ol>
      <li v-for="t in tags" :key="t.tag">
        <RouterLink :to="{ name: 'tag', params: { tag: t.tag } }">#{{ t.tag }}</RouterLink>
        <span class="muted">{{ t.posts }} posts</span>
      </li>
    </ol>
    <p v-if="!tags.length && !error" class="empty">No tagged posts in the last 24 hours.</p>
  </div>
</template>

<style scoped>
ol { margin: 0; padding: 0; list-style: none; display: grid; grid-template-columns: 1fr 1fr; gap: 2px 12px; font-size: 13.5px; }
li { display: flex; justify-content: space-between; gap: 6px; }
li a { font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
p { margin: 0; }
</style>
