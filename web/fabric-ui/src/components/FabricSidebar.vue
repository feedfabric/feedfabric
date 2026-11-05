<script setup lang="ts">
import { resolveDynamicComponent } from 'vue'
import type { SidebarLink, SidebarSection } from '../types'
import FabricBadge from './FabricBadge.vue'
import FabricIcon from './FabricIcon.vue'

// Navigation in titled sections. Uses <RouterLink> when Vue Router is installed, plain links otherwise; the link
// matching activePath (or starting with it, unless exact) is marked as the current page.
const props = defineProps<{ sections: SidebarSection[]; activePath?: string; label?: string }>()
const emit = defineEmits<{ navigate: [] }>()

const linkComponent = resolveDynamicComponent('RouterLink')
const hasRouter = typeof linkComponent !== 'string'

function active(link: SidebarLink) {
  if (!props.activePath) return false
  return link.exact ? props.activePath === link.to : props.activePath === link.to || props.activePath.startsWith(`${link.to}/`)
}
</script>

<template>
  <nav class="fabric-sidebar" :aria-label="label ?? 'Main'" @click="emit('navigate')">
    <div v-for="(s, i) in sections" :key="s.title ?? i" class="section">
      <p v-if="s.title" class="title">{{ s.title }}</p>
      <component :is="hasRouter ? linkComponent : 'a'" v-for="l in s.links" :key="l.to" v-bind="hasRouter ? { to: l.to } : { href: l.to }"
        class="link" :class="{ active: active(l) }" :aria-current="active(l) ? 'page' : undefined">
        <FabricIcon v-if="l.icon" :name="l.icon" :size="16" />
        <span class="label">{{ l.label }}</span>
        <FabricBadge v-if="l.badge != null" size="sm" :tone="active(l) ? 'accent' : 'neutral'">{{ l.badge }}</FabricBadge>
      </component>
    </div>
  </nav>
</template>

<style scoped>
.fabric-sidebar { display: flex; flex-direction: column; gap: 16px; }
.section { display: flex; flex-direction: column; gap: 1px; }
.title { margin: 0 0 4px; padding: 0 10px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .08em; color: var(--fabric-muted); }
.link {
  display: flex; align-items: center; gap: 9px; padding: 6px 10px; border-radius: var(--fabric-radius-md);
  color: var(--fabric-ink); font-size: var(--fabric-text-sm); text-decoration: none; transition: background var(--fabric-fast), color var(--fabric-fast);
}
.link :deep(.fabric-icon) { color: var(--fabric-muted); }
.label { flex: 1; min-width: 0; }
.link:hover { background: var(--fabric-accent-soft); color: var(--fabric-accent); }
.link:hover :deep(.fabric-icon), .link.active :deep(.fabric-icon) { color: var(--fabric-accent); }
.link.active { background: var(--fabric-accent-soft); color: var(--fabric-accent); font-weight: 600; }
.link:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
</style>
