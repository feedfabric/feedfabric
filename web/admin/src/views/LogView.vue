<script setup lang="ts">
import { ref, watch } from 'vue'
import { api } from '../api'
import LogList from '../components/LogList.vue'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import type { LogEntry, ModerationAction } from '../types'

// Everything moderation acted on, newest first: blocked when posted, held for review, held after reports, and
// what staff decided, account suspensions included
const filters: { value: ModerationAction | null; label: string }[] = [
  { value: null, label: 'All' },
  { value: 'blocked', label: 'Blocked' },
  { value: 'held', label: 'Held' },
  { value: 'flagged', label: 'Flagged' },
  { value: 'reported', label: 'Reported' },
  { value: 'reviewed', label: 'Staff actions' },
]
const action = ref<ModerationAction | null>(null)
const entries = ref<LogEntry[] | null>(null)
const now = useNow()
const { error, refresh } = usePolling(async () => {
  entries.value = await api.log(action.value, 200)
}, { intervalMs: 15000 })
watch(action, () => {
  entries.value = null
  refresh()
})
</script>

<template>
  <div class="page-head"><h1>Moderation log</h1></div>
  <div class="segmented" role="tablist" aria-label="Show">
    <button v-for="f in filters" :key="f.label" role="tab" :aria-selected="action === f.value" :class="{ on: action === f.value }"
      @click="action = f.value">{{ f.label }}</button>
  </div>
  <p v-if="error" class="error card">{{ error }}</p>
  <div class="card flush">
    <p v-if="!entries && !error" class="muted pad"><span class="skeleton" /></p>
    <LogList v-else :entries="entries ?? []" :now="now" show-author />
  </div>
</template>

<style scoped>
.segmented { flex-wrap: wrap; align-self: flex-start; border-radius: 12px; }
.pad { padding: 14px 16px; margin: 0; }
</style>
