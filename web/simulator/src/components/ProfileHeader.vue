<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import { persona } from '../format'
import { changed, session } from '../session'
import type { Profile } from '../types'

const props = defineProps<{ profile: Profile }>()

const self = computed(() => props.profile.externalUserId === session.actingAs)
const label = computed(() => persona(props.profile.username))
const pending = ref<boolean | null>(null)
const follows = computed(() => pending.value ?? props.profile.viewerFollows)
const error = ref<string | null>(null)

async function toggleFollow() {
  const next = !follows.value
  pending.value = next
  try {
    await (next ? api.follow(props.profile.externalUserId) : api.unfollow(props.profile.externalUserId))
    error.value = null
    changed()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    setTimeout(() => (pending.value = null), 1500)
  }
}

function actAs() {
  session.actingAs = props.profile.externalUserId
  changed()
}
</script>

<template>
  <div class="profile">
    <div class="avatar">{{ profile.username[0]?.toUpperCase() }}</div>
    <div>
      <div class="name">
        {{ profile.username }}<span v-if="label" class="chip">{{ label }}</span><span v-if="self" class="chip you">you</span>
      </div>
      <div class="stats">
        <RouterLink :to="{ name: 'profile', params: { externalUserId: profile.externalUserId } }"><b>{{ profile.postCount }}</b> posts</RouterLink>
        <RouterLink :to="{ name: 'followers', params: { externalUserId: profile.externalUserId } }"><b>{{ profile.followerCount }}</b> followers</RouterLink>
        <RouterLink :to="{ name: 'following', params: { externalUserId: profile.externalUserId } }"><b>{{ profile.followingCount }}</b> following</RouterLink>
      </div>
    </div>
    <div v-if="!self" class="actions">
      <button :class="{ primary: !follows }" @click="toggleFollow">{{ follows ? 'Unfollow' : 'Follow' }}</button>
      <button @click="actAs">Act as {{ profile.username }}</button>
    </div>
    <p v-if="error" class="error">{{ error }}</p>
  </div>
</template>

<style scoped>
.profile { display: flex; gap: 14px; align-items: center; flex-wrap: wrap; }
.name { font-weight: 650; font-size: 18px; }
.stats { display: flex; gap: 16px; color: var(--muted); font-size: 13px; }
.stats b { color: var(--ink); }
.actions { display: flex; gap: 8px; margin-left: auto; }
.error { flex-basis: 100%; margin: 0; }
</style>
