<script setup lang="ts">
import { ref, watch } from 'vue'
import { api } from '../api'
import type { UserRef } from '../types'

defineProps<{ placeholder: string }>()
const emit = defineEmits<{ pick: [user: UserRef] }>()

const query = ref('')
const results = ref<UserRef[]>([])
const open = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

watch(query, q => {
  clearTimeout(timer)
  if (!q.trim()) {
    results.value = []
    open.value = false
    return
  }
  timer = setTimeout(async () => {
    results.value = await api.searchUsers(q.trim()).catch(() => [])
    open.value = true
  }, 200)
})

function pick(user: UserRef) {
  emit('pick', user)
  query.value = ''
  open.value = false
}
</script>

<template>
  <div class="picker" @focusout="e => !(e.currentTarget as HTMLElement).contains(e.relatedTarget as Node) && (open = false)">
    <input v-model="query" type="search" :placeholder="placeholder" autocomplete="off" @keydown.enter.prevent="results[0] && pick(results[0])"
      @keydown.escape="open = false">
    <div v-if="open" class="results">
      <button v-for="user in results" :key="user.externalUserId" @click="pick(user)">{{ user.username }}</button>
      <span v-if="!results.length" class="empty">No matches</span>
    </div>
  </div>
</template>

<style scoped>
.picker { position: relative; }
input { width: 200px; }
.results { position: absolute; right: 0; z-index: 5; margin-top: 4px; width: 220px; max-height: 260px; overflow: auto; background: var(--card);
  border: 1px solid var(--line); border-radius: 8px; padding: 4px; }
.results button { display: block; width: 100%; text-align: left; border: 0; border-radius: 6px; }
.results button:hover { background: var(--accent-soft); }
.empty { display: block; padding: 6px 8px; }
@media (max-width: 760px) { .results { left: 0; right: auto; } input { width: 160px; } }
</style>
