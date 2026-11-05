<script setup lang="ts">
import { ago, label } from '../format'
import type { LogEntry } from '../types'

// Moderation events, newest first: what happened, to whose content, and what was decided
defineProps<{ entries: LogEntry[]; now: number; showAuthor?: boolean }>()

const tone = (e: LogEntry) =>
  e.decision === 'approved' || e.decision === 'reinstated' ? 'good'
    : e.decision === 'removed' || e.decision === 'suspended' || e.action === 'blocked' ? 'bad'
      : e.authorDeleted ? 'plain' : 'warn'
const outcome = (e: LogEntry) =>
  e.decision ? `${e.decision} by ${e.reviewedBy}` : e.action === 'blocked' ? 'never published'
    : e.authorDeleted ? 'deleted by its author' : 'waiting for review'
// "insult 0.97", "reported by 3 people", "account suspended"
const what = (e: LogEntry) =>
  e.action === 'reported' ? `reported by ${e.score} people` : e.action === 'reviewed' ? label(e.category) : `${label(e.category)} ${e.score.toFixed(2)}`
</script>

<template>
  <ul class="rows">
    <li v-for="e in entries" :key="e.id" class="entry">
      <div class="line">
        <span class="chip">{{ e.kind }} {{ e.action }}</span>
        <span>{{ what(e) }}</span>
        <RouterLink v-if="showAuthor" :to="{ name: 'user', params: { externalUserId: e.author.externalUserId } }" class="who">{{ e.author.username }}</RouterLink>
        <span class="chip" :class="tone(e)">{{ outcome(e) }}</span>
        <RouterLink v-if="e.postId && !e.authorDeleted" :to="{ name: 'post', params: { postId: e.postId } }" class="link">Open</RouterLink>
        <span class="muted when">{{ ago(e.createdAt, now) }}</span>
      </div>
      <p v-if="e.excerpt" class="excerpt">“{{ e.excerpt }}”</p>
    </li>
    <li v-if="!entries.length" class="muted">Nothing yet.</li>
  </ul>
</template>

<style scoped>
.entry { display: block; }
.line { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
.who { font-weight: 600; }
.when { margin-left: auto; }
.excerpt { margin: 4px 0 0; color: var(--muted); font-size: 14px; overflow-wrap: anywhere; }
</style>
