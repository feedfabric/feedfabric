<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'
import Pager from '../components/Pager.vue'
import { useNow } from '../composables/useNow'
import { useRouteQuery } from '../composables/useRouteQuery'
import { ago, fmt, statusTone } from '../format'
import type { AdminPost, ContentState, Page } from '../types'

// Every post in every state, newest first: search the text, narrow to an author, a status, or reported ones
const PageSize = 50
const router = useRouter()
const now = useNow()
const q = useRouteQuery('q')
const author = useRouteQuery('author')
const status = useRouteQuery('status')
const reported = useRouteQuery('reported')
const pageText = useRouteQuery('page', '1')
const page = computed(() => Math.max(1, Number(pageText.value) || 1))
const search = ref(q.value)
const authorSearch = ref(author.value)
const result = ref<Page<AdminPost> | null>(null)
const error = ref<string | null>(null)

let timer: ReturnType<typeof setTimeout> | undefined
watch([search, authorSearch], ([text, name]) => {
  clearTimeout(timer)
  timer = setTimeout(() => {
    q.value = text.trim()
    author.value = name.trim()
  }, 350)
})

async function load() {
  try {
    result.value = await api.posts({
      q: q.value, author: author.value, status: (status.value || null) as ContentState | null, reported: reported.value === 'true',
      offset: (page.value - 1) * PageSize, limit: PageSize,
    })
    error.value = null
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}
watch([q, author, status, reported, page], load, { immediate: true })

const statuses = [
  { value: '', label: 'All' }, { value: 'visible', label: 'Visible' }, { value: 'held', label: 'Held' },
  { value: 'removed', label: 'Removed' }, { value: 'deleted', label: 'Deleted' },
]
</script>

<template>
  <div class="page-head">
    <h1>Posts</h1>
    <span v-if="result" class="muted">{{ fmt(result.total) }} {{ result.total === 1 ? 'post' : 'posts' }}</span>
  </div>

  <div class="toolbar">
    <input v-model="search" type="search" placeholder="Search the text" aria-label="Search post text" class="search">
    <input v-model="authorSearch" type="search" placeholder="Author username or ID" aria-label="Author" class="author">
    <div class="segmented" role="group" aria-label="Status">
      <button v-for="s in statuses" :key="s.value" :class="{ on: status === s.value }" :aria-pressed="status === s.value" @click="status = s.value">{{ s.label }}</button>
    </div>
    <label class="check"><input type="checkbox" :checked="reported === 'true'" @change="reported = ($event.target as HTMLInputElement).checked ? 'true' : ''"> Reported only</label>
  </div>

  <p v-if="error" class="error card">{{ error }}</p>
  <div class="card flush">
    <div class="table-wrap">
      <table>
        <thead>
          <tr><th>Post</th><th>Author</th><th>Status</th><th class="num">Likes</th><th class="num">Comments</th><th class="num">Reports</th><th class="num">Posted</th></tr>
        </thead>
        <tbody>
          <tr v-if="!result"><td colspan="7"><span class="skeleton" /></td></tr>
          <tr v-else-if="!result.items.length"><td colspan="7" class="muted">No posts match.</td></tr>
          <tr v-for="p in result?.items ?? []" :key="p.id" class="clickable" @click="router.push({ name: 'post', params: { postId: p.id } })">
            <td class="text"><RouterLink :to="{ name: 'post', params: { postId: p.id } }">{{ p.content }}</RouterLink></td>
            <td><RouterLink :to="{ name: 'user', params: { externalUserId: p.author.externalUserId } }" @click.stop>{{ p.author.username }}</RouterLink></td>
            <td><span class="chip" :class="statusTone(p.status)">{{ p.status }}</span></td>
            <td class="num">{{ fmt(p.likes) }}</td>
            <td class="num">{{ fmt(p.comments) }}</td>
            <td class="num" :class="{ hot: p.reports }">{{ fmt(p.reports) }}</td>
            <td class="num muted">{{ ago(p.createdAt, now) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <Pager v-if="result" :page="page" :size="PageSize" :total="result.total" :shown="result.items.length" @go="p => (pageText = String(p))" />
  </div>
</template>

<style scoped>
.search { width: 260px; max-width: 100%; }
.author { width: 200px; max-width: 100%; }
.check { display: flex; align-items: center; gap: 6px; font-size: 14px; }
.text { min-width: 260px; max-width: 520px; overflow-wrap: anywhere; }
.hot { color: var(--bad); font-weight: 600; }
</style>
