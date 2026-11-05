<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'
import Pager from '../components/Pager.vue'
import { useRouteQuery } from '../composables/useRouteQuery'
import { fmt, statusTone } from '../format'
import type { AdminUser, Page, UserStatus } from '../types'

// Every user, searchable by username, display name or ID. Sorting by reports or flags brings problem accounts up.
const PageSize = 50
const router = useRouter()
const q = useRouteQuery('q')
const status = useRouteQuery('status')
const sort = useRouteQuery('sort', 'username')
const pageText = useRouteQuery('page', '1')
const page = computed(() => Math.max(1, Number(pageText.value) || 1))
const search = ref(q.value)
const result = ref<Page<AdminUser> | null>(null)
const error = ref<string | null>(null)

let timer: ReturnType<typeof setTimeout> | undefined
watch(search, value => {
  clearTimeout(timer)
  timer = setTimeout(() => (q.value = value.trim()), 300)
})

async function load() {
  try {
    result.value = await api.users({
      q: q.value, status: (status.value || null) as UserStatus | null, sort: sort.value as 'username',
      offset: (page.value - 1) * PageSize, limit: PageSize,
    })
    error.value = null
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}
watch([q, status, sort, page], load, { immediate: true })

const sorts = [
  { value: 'username', label: 'Name' }, { value: 'posts', label: 'Posts' }, { value: 'reports', label: 'Reported' }, { value: 'flags', label: 'Flagged' },
]
const statuses = [{ value: '', label: 'All' }, { value: 'active', label: 'Active' }, { value: 'suspended', label: 'Suspended' }]
</script>

<template>
  <div class="page-head">
    <h1>Users</h1>
    <span v-if="result" class="muted">{{ fmt(result.total) }} {{ result.total === 1 ? 'user' : 'users' }}</span>
  </div>

  <div class="toolbar">
    <input v-model="search" type="search" placeholder="Search username, name or ID" aria-label="Search users" class="search">
    <div class="segmented" role="group" aria-label="Status">
      <button v-for="s in statuses" :key="s.value" :class="{ on: status === s.value }" :aria-pressed="status === s.value" @click="status = s.value">{{ s.label }}</button>
    </div>
    <label class="muted">Sort
      <select v-model="sort"><option v-for="s in sorts" :key="s.value" :value="s.value">{{ s.label }}</option></select>
    </label>
  </div>

  <p v-if="error" class="error card">{{ error }}</p>
  <div class="card flush">
    <div class="table-wrap">
      <table>
        <thead>
          <tr><th>User</th><th>Status</th><th class="num">Posts</th><th class="num">Followers</th><th class="num">Reported</th><th class="num">Flagged</th></tr>
        </thead>
        <tbody>
          <tr v-if="!result"><td colspan="6"><span class="skeleton" /></td></tr>
          <tr v-else-if="!result.items.length"><td colspan="6" class="muted">No users match.</td></tr>
          <tr v-for="u in result?.items ?? []" :key="u.externalUserId" class="clickable" @click="router.push({ name: 'user', params: { externalUserId: u.externalUserId } })">
            <td>
              <RouterLink :to="{ name: 'user', params: { externalUserId: u.externalUserId } }" class="name">{{ u.username }}</RouterLink>
              <span v-if="u.isVerified" class="chip">verified</span>
              <div class="muted">{{ u.displayName ? `${u.displayName} · ` : '' }}{{ u.externalUserId }}</div>
            </td>
            <td><span class="chip" :class="statusTone(u.status)">{{ u.status }}</span></td>
            <td class="num">{{ fmt(u.posts) }}</td>
            <td class="num">{{ fmt(u.followers) }}</td>
            <td class="num" :class="{ hot: u.reportsAgainst }">{{ fmt(u.reportsAgainst) }}</td>
            <td class="num" :class="{ hot: u.flags }">{{ fmt(u.flags) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <Pager v-if="result" :page="page" :size="PageSize" :total="result.total" :shown="result.items.length" @go="p => (pageText = String(p))" />
  </div>
</template>

<style scoped>
.search { width: 280px; max-width: 100%; }
.name { font-weight: 600; }
.hot { color: var(--bad); font-weight: 600; }
</style>
