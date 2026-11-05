<script setup lang="ts">
import { ago, label, statusTone } from '../format'
import type { AdminReport } from '../types'

// Reports from members: who reported what, why, and what it looks like now
defineProps<{ reports: AdminReport[]; now: number; showTarget?: boolean }>()
</script>

<template>
  <ul class="rows">
    <li v-for="r in reports" :key="r.id" class="report">
      <div class="line">
        <span class="chip warn">{{ label(r.reason) }}</span>
        <span>
          <RouterLink :to="{ name: 'user', params: { externalUserId: r.reporter.externalUserId } }" class="who">{{ r.reporter.username }}</RouterLink>
          reported
          <template v-if="showTarget">
            <RouterLink :to="{ name: 'user', params: { externalUserId: r.targetAuthor.externalUserId } }" class="who">{{ r.targetAuthor.username }}</RouterLink>'s
          </template>
          <template v-else>this</template>
          {{ r.kind }}
        </span>
        <span class="chip" :class="statusTone(r.targetStatus)">now {{ r.targetStatus }}</span>
        <RouterLink :to="{ name: 'post', params: { postId: r.postId } }" class="link">Open</RouterLink>
        <span class="muted when">{{ ago(r.createdAt, now) }}</span>
      </div>
      <p v-if="showTarget" class="excerpt">“{{ r.excerpt }}”</p>
    </li>
    <li v-if="!reports.length" class="muted">No reports.</li>
  </ul>
</template>

<style scoped>
.report { display: block; }
.line { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
.who { font-weight: 600; }
.when { margin-left: auto; }
.excerpt { margin: 4px 0 0; color: var(--muted); font-size: 14px; overflow-wrap: anywhere; }
</style>
