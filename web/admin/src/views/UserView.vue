<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import LogList from '../components/LogList.vue'
import ReportList from '../components/ReportList.vue'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { ago, fmt, statusTone } from '../format'
import { changed } from '../session'
import type { AdminUserDetail } from '../types'

// One user: what they've posted and said, what's been reported, and what moderation has done. Staff can suspend
// (they can read but not post or interact, and their posts leave feeds) or reinstate, and mark them verified.
const props = defineProps<{ externalUserId: string }>()
const detail = ref<AdminUserDetail | null>(null)
const missing = ref(false)
const busy = ref(false)
const actionError = ref<string | null>(null)
const now = useNow()

const { error } = usePolling(async () => {
  try {
    detail.value = await api.user(props.externalUserId)
    missing.value = false
  } catch (e) {
    if ((e as { status?: number }).status === 404) {
      missing.value = true
      return
    }
    throw e
  }
}, { intervalMs: 15000 })

const user = computed(() => detail.value?.user)
const stats = computed(() => detail.value && [
  { label: 'Posts', value: detail.value.user.posts },
  { label: 'Comments', value: detail.value.comments },
  { label: 'Likes given', value: detail.value.likes },
  { label: 'Followers', value: detail.value.user.followers },
  { label: 'Following', value: detail.value.following },
  { label: 'Reported', value: detail.value.user.reportsAgainst, hot: true },
  { label: 'Flagged', value: detail.value.user.flags, hot: true },
])

async function act(run: () => Promise<void>) {
  busy.value = true
  try {
    await run()
    actionError.value = null
    changed()
  } catch (e) {
    actionError.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

function toggleSuspension() {
  if (!user.value) return
  const suspend = user.value.status !== 'suspended'
  if (suspend && !confirm(`Suspend ${user.value.username}? They can still read, but can't post or interact, and their posts leave feeds.`)) return
  act(() => api.setUserStatus(props.externalUserId, suspend ? 'suspended' : 'active'))
}

const toggleVerified = () => user.value && act(() => api.setVerified(props.externalUserId, !user.value!.isVerified))
</script>

<template>
  <p v-if="missing" class="card muted">No user with the ID {{ externalUserId }}.</p>
  <template v-else>
    <div class="page-head">
      <div>
        <h1>{{ user?.username ?? externalUserId }}</h1>
        <p v-if="user" class="muted sub">
          {{ user.displayName ? `${user.displayName} · ` : '' }}ID {{ user.externalUserId }} · profile updated {{ ago(user.syncedAt, now) }}
        </p>
      </div>
      <div v-if="user" class="actions">
        <span class="chip" :class="statusTone(user.status)">{{ user.status }}</span>
        <span v-if="user.isVerified" class="chip">verified</span>
        <button :disabled="busy" @click="toggleVerified">{{ user.isVerified ? 'Remove verification' : 'Verify' }}</button>
        <button :class="user.status === 'suspended' ? 'good' : 'danger'" :disabled="busy" @click="toggleSuspension">
          {{ user.status === 'suspended' ? 'Reinstate' : 'Suspend' }}
        </button>
      </div>
    </div>
    <p v-if="error || actionError" class="error card">{{ error || actionError }}</p>

    <section v-if="stats" class="stats" aria-label="Activity">
      <div v-for="s in stats" :key="s.label" class="stat card">
        <span class="muted">{{ s.label }}</span>
        <strong :class="{ hot: s.hot && s.value }">{{ fmt(s.value) }}</strong>
      </div>
    </section>

    <div v-if="detail" class="grid two">
      <section class="card flush">
        <h2>Recent posts</h2>
        <ul class="rows">
          <li v-for="p in detail.recentPosts" :key="p.id">
            <RouterLink :to="{ name: 'post', params: { postId: p.id } }" class="text">{{ p.content }}</RouterLink>
            <span class="meta">
              <span v-if="p.status !== 'visible'" class="chip" :class="statusTone(p.status)">{{ p.status }}</span>
              <span class="muted">{{ ago(p.createdAt, now) }}</span>
            </span>
          </li>
          <li v-if="!detail.recentPosts.length" class="muted">No posts.</li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Recent comments</h2>
        <ul class="rows">
          <li v-for="c in detail.recentComments" :key="c.id">
            <RouterLink :to="{ name: 'post', params: { postId: c.postId } }" class="text">{{ c.content }}</RouterLink>
            <span class="meta">
              <span v-if="c.status !== 'visible'" class="chip" :class="statusTone(c.status)">{{ c.status }}</span>
              <span class="muted">{{ ago(c.createdAt, now) }}</span>
            </span>
          </li>
          <li v-if="!detail.recentComments.length" class="muted">No comments.</li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Reports about them</h2>
        <ReportList :reports="detail.reportsAgainst" :now="now" />
      </section>
      <section class="card flush">
        <h2>Moderation history</h2>
        <LogList :entries="detail.history" :now="now" />
      </section>
    </div>
    <p v-else-if="!error" class="card"><span class="skeleton" /></p>
  </template>
</template>

<style scoped>
.sub { margin: 2px 0 0; }
.actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 8px; }
.stat { display: flex; flex-direction: column; padding: 10px 14px; }
.stat strong { font-size: 20px; font-variant-numeric: tabular-nums; }
.hot { color: var(--bad); }
.text { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.meta { display: flex; align-items: baseline; gap: 6px; flex: none; }
</style>
