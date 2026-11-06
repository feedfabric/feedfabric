<script setup lang="ts">
import FabricIcon from './FabricIcon.vue'

// Something to show or hide (v-model:open), built on <details> so it works with the keyboard and find-in-page
defineProps<{ summary: string; hint?: string }>()
const open = defineModel<boolean>('open', { default: false })

function toggled(event: Event) {
  open.value = (event.target as HTMLDetailsElement).open
}
</script>

<template>
  <details class="fabric-disclosure" :open="open" @toggle="toggled">
    <summary>
      <FabricIcon name="chevron-right" :size="16" class="chevron" />
      <span class="summary">{{ summary }}</span>
      <span v-if="hint" class="hint">{{ hint }}</span>
    </summary>
    <div class="content"><slot /></div>
  </details>
</template>

<style scoped>
.fabric-disclosure { border-radius: var(--fabric-radius-md); }
summary {
  display: flex; align-items: center; gap: 6px; padding: 6px 4px; list-style: none; cursor: pointer;
  font-size: var(--fabric-text-sm); font-weight: 600; border-radius: var(--fabric-radius-sm);
}
summary::-webkit-details-marker { display: none; }
summary:hover { color: var(--fabric-accent); }
summary:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
.chevron { transition: transform var(--fabric-fast) var(--fabric-ease); color: var(--fabric-muted); }
details[open] .chevron { transform: rotate(90deg); }
.hint { margin-left: auto; font-weight: 400; color: var(--fabric-muted); font-size: var(--fabric-text-xs); }
.content { padding: 4px 4px 8px 26px; }
</style>
