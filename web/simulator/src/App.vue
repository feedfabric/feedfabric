<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded } from 'vue-router'
import AppHeader from './components/AppHeader.vue'
import ActivityPanel from './components/ActivityPanel.vue'
import TrendingPanel from './components/TrendingPanel.vue'

const route = useRoute()
// The dashboard and studio need the whole width; every other page has the activity down the side
const wide = computed(() => route.name === 'dashboard' || route.name === 'studio')

// Feeds stay alive when you open a post, so Back shows the same posts where you left them.
// A profile is one page across its Posts / Followers / Following sections.
const cached = ['HomeView', 'ProfileView', 'TagView', 'MentionsView']
const pageKey = (r: RouteLocationNormalizedLoaded) => (r.params.externalUserId ? `/u/${r.params.externalUserId}` : r.fullPath)
</script>

<template>
  <AppHeader />
  <main class="layout" :class="{ wide }">
    <section>
      <RouterView v-slot="{ Component, route: current }">
        <KeepAlive :include="cached" :max="6">
          <component :is="Component" :key="pageKey(current)" />
        </KeepAlive>
      </RouterView>
    </section>
    <aside v-if="!wide" class="stack">
      <TrendingPanel />
      <ActivityPanel />
    </aside>
  </main>
</template>
