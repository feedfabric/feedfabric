<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import {
  FabricAppShell, FabricBadge, FabricInput, FabricSidebar, FabricStack, FabricThemeToggle, FabricToaster, type SidebarSection,
} from '@feedfabric/fabric-ui'
import { loadMe, me, meError } from './me'
import { session } from './session'

// The admin panel: a sidebar of sections, each shown only to the staff role that can use it
const route = useRoute()
onMounted(loadMe)

const sections = computed<SidebarSection[]>(() => [
  {
    title: 'Overview',
    show: true,
    links: [
      { to: '/overview', label: 'Dashboard', icon: 'chart' as const },
      { to: '/feed', label: 'Feeds', icon: 'eye' as const },
    ],
  },
  {
    title: 'Moderation',
    show: !!me.value?.moderator,
    links: [
      { to: '/review', label: 'Review queue', icon: 'shield' as const },
      { to: '/reports', label: 'Reports', icon: 'flag' as const },
      { to: '/log', label: 'Moderation log', icon: 'clock' as const },
    ],
  },
  {
    title: 'Community',
    show: !!me.value?.moderator,
    links: [
      { to: '/users', label: 'Users', icon: 'users' as const },
      { to: '/posts', label: 'Posts', icon: 'message' as const },
      { to: '/compose', label: 'New post', icon: 'edit' as const },
    ],
  },
  { title: 'Ranking', show: !!me.value?.rankingAdmin, links: [{ to: '/studio', label: 'Feed studio', icon: 'sparkle' as const }] },
  {
    title: 'Integrations',
    show: !!me.value?.integrationAdmin,
    links: [
      { to: '/api-keys', label: 'API keys', icon: 'shield' as const },
      { to: '/data-types', label: 'Data types', icon: 'filter' as const },
    ],
  },
  {
    title: 'System',
    show: true,
    links: [
      { to: '/system', label: 'Settings and models', icon: 'settings' as const },
      { to: '/api', label: 'API reference', icon: 'monitor' as const },
    ],
  },
].filter(s => s.show).map(({ title, links }) => ({ title, links })))

const isDevelopment = computed(() => me.value?.environment === 'Development')
const who = computed(() => me.value?.username ?? me.value?.externalUserId ?? '')
</script>

<template>
  <FabricAppShell :route-key="route.path">
    <template #brand>
      <RouterLink to="/overview" class="brand">FeedFabric <span>admin</span></RouterLink>
    </template>
    <template #sidebar="{ closeMenu }">
      <FabricSidebar :sections="sections" :active-path="route.path" label="Admin" @navigate="closeMenu" />
    </template>
    <template #sidebar-footer>
      <div v-if="me" class="who">
        <span>Signed in as <b>{{ who }}</b></span>
        <FabricStack direction="row" wrap :gap="1">
          <FabricBadge v-if="me.moderator" tone="accent" size="sm">moderator</FabricBadge>
          <FabricBadge v-if="me.rankingAdmin" tone="accent" size="sm">ranking</FabricBadge>
          <FabricBadge v-if="isDevelopment" tone="warn" size="sm">development</FabricBadge>
        </FabricStack>
      </div>
      <p v-else-if="meError" class="error">{{ meError }}</p>
      <FabricInput v-model.trim="session.token" label="Bearer token" type="password" size="sm" placeholder="From your identity provider"
        autocomplete="off" />
      <FabricInput v-if="!session.token" v-model.trim="session.devUser" label="Development user" size="sm"
        hint="Development only: what you do is recorded against this user." />
      <FabricThemeToggle />
    </template>
    <div class="page">
      <RouterView :key="route.fullPath.split('?')[0]" />
    </div>
    <FabricToaster />
  </FabricAppShell>
</template>

<style scoped>
.brand { font-weight: 700; color: var(--fabric-ink); }
.brand span { color: var(--fabric-accent); }
.who { display: flex; flex-direction: column; gap: 4px; font-size: var(--fabric-text-sm); color: var(--fabric-muted); }
.who b { color: var(--fabric-ink); }
</style>
