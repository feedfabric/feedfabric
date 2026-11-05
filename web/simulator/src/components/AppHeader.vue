<script setup lang="ts">
import { watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'
import { changed, session } from '../session'
import type { UserRef } from '../types'
import UserPicker from './UserPicker.vue'

const router = useRouter()

watch(() => session.actingAs, async externalUserId => {
  session.actingAsName = externalUserId
  session.actingAsName = (await api.profile(externalUserId).catch(() => null))?.username ?? externalUserId
}, { immediate: true })

function view(user: UserRef) {
  router.push({ name: 'profile', params: { externalUserId: user.externalUserId } })
}

function actAs(user: UserRef) {
  session.actingAs = user.externalUserId
  changed()
}
</script>

<template>
  <header>
    <div class="bar">
      <RouterLink to="/live" class="brand">FeedFabric simulator</RouterLink>
      <nav>
        <RouterLink to="/live" title="Open the profile of whoever posted most recently">Latest poster</RouterLink>
        <RouterLink to="/home">Home feed</RouterLink>
        <RouterLink to="/mentions">Mentions</RouterLink>
        <RouterLink to="/dashboard">Dashboard</RouterLink>
        <RouterLink to="/studio">Feed studio</RouterLink>
        <RouterLink :to="{ name: 'profile', params: { externalUserId: session.actingAs } }">My profile</RouterLink>
      </nav>
      <div class="grow" />
      <UserPicker placeholder="Find a user, e.g. runs_1" @pick="view" />
      <div class="me">
        <span>Acting as <b>{{ session.actingAsName }}</b></span>
        <UserPicker placeholder="Switch to…" @pick="actAs" />
      </div>
    </div>
  </header>
</template>

<style scoped>
header { position: sticky; top: 0; z-index: 4; background: var(--bg); border-bottom: 1px solid var(--line); }
.bar { max-width: 1080px; margin: 0 auto; padding: 10px 16px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.brand { font-weight: 700; letter-spacing: -.01em; margin-right: 4px; }
nav { display: flex; gap: 4px; }
nav a { padding: 6px 10px; border-radius: 8px; color: var(--muted); }
nav a.router-link-active { color: var(--ink); background: var(--card); box-shadow: inset 0 0 0 1px var(--line); }
.grow { flex: 1; }
.me { display: flex; align-items: center; gap: 8px; padding: 4px 4px 4px 12px; border-radius: 99px; background: var(--me-soft); color: var(--me); font-size: 14px; }
.me :deep(input) { width: 130px; padding: 4px 10px; border-radius: 99px; }
</style>
