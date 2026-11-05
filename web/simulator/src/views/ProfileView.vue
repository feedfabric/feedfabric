<script setup lang="ts">
import { ref, toRef } from 'vue'
import { api, ApiError } from '../api'
import { usePolling } from '../composables/usePolling'
import type { Profile } from '../types'
import ProfileHeader from '../components/ProfileHeader.vue'

const props = defineProps<{ externalUserId: string }>()
const profile = ref<Profile | null>(null)
const missing = ref(false)

const { error } = usePolling(async () => {
  try {
    const next = await api.profile(props.externalUserId)
    // A slow response for the previous user mustn't overwrite the one now in the URL
    if (next.externalUserId !== props.externalUserId) return
    profile.value = next
    missing.value = false
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) {
      profile.value = null
      missing.value = true
      return
    }
    throw e
  }
}, { watch: [toRef(props, 'externalUserId')] })
</script>

<template>
  <div class="stack">
    <div class="card">
      <p v-if="missing" class="muted">No user called {{ externalUserId }}.</p>
      <ProfileHeader v-else-if="profile && profile.externalUserId === externalUserId" :profile="profile" />
      <p v-else-if="!error" class="muted">Loading…</p>
      <p v-if="error" class="error">{{ error }}</p>
    </div>
    <div v-if="profile && profile.externalUserId === externalUserId">
      <nav class="tabs">
        <RouterLink :to="{ name: 'profile', params: { externalUserId } }">Posts</RouterLink>
        <RouterLink :to="{ name: 'followers', params: { externalUserId } }">Followers ({{ profile.followerCount }})</RouterLink>
        <RouterLink :to="{ name: 'following', params: { externalUserId } }">Following ({{ profile.followingCount }})</RouterLink>
      </nav>
      <div class="card tabbed">
        <RouterView v-slot="{ Component }">
          <component :is="Component" :profile="profile" />
        </RouterView>
      </div>
    </div>
  </div>
</template>
