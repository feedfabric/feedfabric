<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import ReportList from '../components/ReportList.vue'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { label } from '../format'
import type { AdminReport } from '../types'

// What members are reporting, newest first. Enough reports on one thing hold it for review automatically; this is
// where to see the reports that haven't got there yet, and who reports what.
const reports = ref<AdminReport[] | null>(null)
const reason = ref('')
const now = useNow()
const { error } = usePolling(async () => {
  reports.value = await api.reports(300)
}, { intervalMs: 15000 })

const reasons = computed(() => {
  const counts = new Map<string, number>()
  for (const r of reports.value ?? []) counts.set(r.reason, (counts.get(r.reason) ?? 0) + 1)
  return [...counts].sort((a, b) => b[1] - a[1])
})
const shown = computed(() => (reports.value ?? []).filter(r => !reason.value || r.reason === reason.value))
</script>

<template>
  <div class="page-head">
    <h1>Reports</h1>
    <span class="muted">The latest 300</span>
  </div>
  <div v-if="reasons.length" class="segmented" role="group" aria-label="Reason">
    <button :class="{ on: !reason }" :aria-pressed="!reason" @click="reason = ''">All</button>
    <button v-for="[r, n] in reasons" :key="r" :class="{ on: reason === r }" :aria-pressed="reason === r" @click="reason = r">{{ label(r) }} · {{ n }}</button>
  </div>
  <p v-if="error" class="error card">{{ error }}</p>
  <div class="card flush">
    <p v-if="!reports && !error" class="muted pad"><span class="skeleton" /></p>
    <ReportList v-else :reports="shown" :now="now" show-target />
  </div>
</template>

<style scoped>
.segmented { flex-wrap: wrap; align-self: flex-start; border-radius: 12px; }
.pad { padding: 14px 16px; margin: 0; }
</style>
