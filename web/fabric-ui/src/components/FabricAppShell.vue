<script setup lang="ts">
import { ref, watch } from 'vue'
import FabricButton from './FabricButton.vue'

// The frame of an app: a sidebar with the brand, navigation and footer (account, theme) beside the main content.
// Below 760px the sidebar folds into a top bar with a menu button. A skip link lets keyboard users go straight to
// the content. The folded menu closes when routeKey (the current path) changes, or when the sidebar slot calls
// closeMenu, which FabricSidebar's navigate event is for: tapping the page you're already on changes no route.
const props = defineProps<{ routeKey?: string }>()
const menuOpen = ref(false)
watch(() => props.routeKey, () => (menuOpen.value = false))
</script>

<template>
  <div class="fabric-shell" :class="{ open: menuOpen }">
    <a class="skip" href="#fabric-main">Skip to content</a>
    <aside class="side">
      <div class="top">
        <div class="brand"><slot name="brand" /></div>
        <FabricButton class="menu" variant="ghost" :icon="menuOpen ? 'x' : 'menu'" :label="menuOpen ? 'Close menu' : 'Menu'"
          :aria-expanded="menuOpen" aria-controls="fabric-shell-nav" @click="menuOpen = !menuOpen" />
      </div>
      <div id="fabric-shell-nav" class="nav"><slot name="sidebar" :close-menu="() => (menuOpen = false)" /></div>
      <div v-if="$slots['sidebar-footer']" class="foot"><slot name="sidebar-footer" /></div>
    </aside>
    <main id="fabric-main" tabindex="-1" class="main"><slot /></main>
  </div>
</template>

<style scoped>
.fabric-shell { display: grid; grid-template-columns: 236px minmax(0, 1fr); min-height: 100dvh; background: var(--fabric-bg); }
.side {
  position: sticky; top: 0; height: 100dvh; display: flex; flex-direction: column; gap: 18px; padding: 18px 12px;
  background: var(--fabric-sunken); border-right: 1px solid var(--fabric-line); overflow-y: auto;
}
.top { display: flex; align-items: center; justify-content: space-between; padding: 0 6px; }
.brand { font-weight: 700; font-size: var(--fabric-text-lg); letter-spacing: -.01em; }
.menu { display: none; }
.nav { flex: 1; }
.foot { display: flex; flex-direction: column; gap: 10px; padding-top: 12px; border-top: 1px solid var(--fabric-line); }
.main { min-width: 0; width: 100%; max-width: 1240px; padding: 24px 28px 48px; outline: none; }
.skip { position: absolute; left: 8px; top: -48px; z-index: 100; padding: 8px 12px; border-radius: var(--fabric-radius-md);
  background: var(--fabric-accent); color: var(--fabric-on-accent); text-decoration: none; }
.skip:focus { top: 8px; }

@media (max-width: 760px) {
  .fabric-shell { grid-template-columns: minmax(0, 1fr); }
  .side { position: sticky; z-index: 20; height: auto; max-height: 100dvh; padding: 10px 12px; gap: 0; border-right: 0; border-bottom: 1px solid var(--fabric-line); }
  .menu { display: inline-flex; }
  .nav, .foot { display: none; }
  .open .nav, .open .foot { display: flex; flex-direction: column; margin-top: 14px; }
  .main { padding: 16px; }
}
</style>
