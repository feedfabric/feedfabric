<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { loadMe, me, meError } from './me'
import { session } from './session'

// The admin panel: a sidebar of sections, each shown only to the staff role that can use it
const route = useRoute()
const menuOpen = ref(false)
onMounted(loadMe)

const sections = computed(() => [
  { title: 'Overview', show: true, links: [{ to: '/overview', label: 'Dashboard' }] },
  {
    title: 'Moderation',
    show: !!me.value?.moderator,
    links: [{ to: '/review', label: 'Review queue' }, { to: '/reports', label: 'Reports' }, { to: '/log', label: 'Moderation log' }],
  },
  { title: 'Community', show: !!me.value?.moderator, links: [{ to: '/users', label: 'Users' }, { to: '/posts', label: 'Posts' }] },
  { title: 'Ranking', show: !!me.value?.rankingAdmin, links: [{ to: '/studio', label: 'Feed studio' }] },
  { title: 'System', show: true, links: [{ to: '/system', label: 'Settings and models' }] },
].filter(s => s.show))

const isDevelopment = computed(() => me.value?.environment === 'Development')
const who = computed(() => me.value?.username ?? me.value?.externalUserId ?? '')
</script>

<template>
  <div class="shell" :class="{ open: menuOpen }">
    <aside>
      <div class="top">
        <RouterLink to="/overview" class="brand">FeedFabric <span>admin</span></RouterLink>
        <button class="plain menu" :aria-expanded="menuOpen" aria-controls="nav" @click="menuOpen = !menuOpen">{{ menuOpen ? 'Close' : 'Menu' }}</button>
      </div>
      <nav id="nav" @click="menuOpen = false">
        <div v-for="s in sections" :key="s.title" class="section">
          <span class="title">{{ s.title }}</span>
          <RouterLink v-for="l in s.links" :key="l.to" :to="l.to" :class="{ active: route.path.startsWith(l.to) }">{{ l.label }}</RouterLink>
        </div>
      </nav>
      <div class="signin">
        <p v-if="me" class="muted">
          Signed in as <b>{{ who }}</b>
          <span v-if="me.moderator" class="chip">moderator</span>
          <span v-if="me.rankingAdmin" class="chip">ranking</span>
          <span v-if="isDevelopment" class="chip warn">development</span>
        </p>
        <p v-else-if="meError" class="error">{{ meError }}</p>
        <label>
          <span class="muted">Bearer token</span>
          <input v-model.trim="session.token" type="password" placeholder="From your identity provider" autocomplete="off">
        </label>
        <label v-if="!session.token" title="Development only: decisions are recorded against this user">
          <span class="muted">Development user</span>
          <input v-model.trim="session.devUser">
        </label>
      </div>
    </aside>
    <main>
      <RouterView :key="route.fullPath.split('?')[0]" />
    </main>
  </div>
</template>

<style scoped>
.shell { display: grid; grid-template-columns: 232px minmax(0, 1fr); min-height: 100vh; }
aside { position: sticky; top: 0; height: 100vh; overflow-y: auto; display: flex; flex-direction: column; gap: 16px; padding: 16px 12px;
  background: var(--side); border-right: 1px solid var(--line); }
.top { display: flex; align-items: center; justify-content: space-between; padding: 0 6px; }
.brand { font-weight: 700; font-size: 16px; }
.brand span { color: var(--accent); }
.menu { display: none; color: var(--muted); }
nav { display: flex; flex-direction: column; gap: 14px; }
.section { display: flex; flex-direction: column; gap: 1px; }
.title { font-size: 11px; text-transform: uppercase; letter-spacing: .07em; color: var(--muted); padding: 0 8px 4px; }
nav a { padding: 6px 8px; border-radius: 8px; color: var(--ink); font-size: 14px; }
nav a:hover { background: var(--accent-soft); color: var(--accent); }
nav a.active { background: var(--accent-soft); color: var(--accent); font-weight: 600; }
.signin { margin-top: auto; display: flex; flex-direction: column; gap: 8px; padding: 12px 8px 0; border-top: 1px solid var(--line); }
.signin p { margin: 0; display: flex; flex-wrap: wrap; gap: 4px; align-items: baseline; }
.signin label { display: flex; flex-direction: column; gap: 2px; }
.signin input { width: 100%; }
@media (max-width: 860px) {
  .shell { grid-template-columns: 1fr; }
  aside { position: static; height: auto; border-right: 0; border-bottom: 1px solid var(--line); gap: 0; padding: 10px 12px; }
  .menu { display: inline; }
  nav, .signin { display: none; }
  .shell.open nav, .shell.open .signin { display: flex; margin-top: 12px; }
}
</style>
