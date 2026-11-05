<script setup lang="ts">
import { computed } from 'vue'
import { persona } from '../format'
import type { Profile } from '../types'

const props = defineProps<{ profile: Profile; kind: 'followers' | 'following' }>()
const users = computed(() => props.kind === 'followers' ? props.profile.followers : props.profile.following)
const total = computed(() => props.kind === 'followers' ? props.profile.followerCount : props.profile.followingCount)
</script>

<template>
  <ul>
    <li v-for="user in users" :key="user.externalUserId">
      <RouterLink :to="{ name: 'profile', params: { externalUserId: user.externalUserId } }">{{ user.username }}</RouterLink>
      <span v-if="persona(user.username)" class="chip">{{ persona(user.username) }}</span>
    </li>
  </ul>
  <p v-if="!users.length" class="empty">{{ kind === 'followers' ? 'Nobody follows them yet.' : 'Not following anyone.' }}</p>
  <p v-else-if="total > users.length" class="muted">Showing the first {{ users.length }} of {{ total }}, by username.</p>
</template>

<style scoped>
ul { list-style: none; margin: 0; padding: 0; columns: 2; }
li { padding: 3px 0; break-inside: avoid; }
@media (max-width: 760px) { ul { columns: 1; } }
</style>
