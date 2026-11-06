<script setup lang="ts" generic="T extends string | number">
import { useId } from 'vue'
import type { TabItem } from '../types'
import FabricBadge from './FabricBadge.vue'
import FabricIcon from './FabricIcon.vue'

// Panels of one page, one at a time (v-model). The tab list follows the ARIA tabs pattern: arrows move and select,
// Home and End jump; the #panel slot gets the selected value and is labelled by its tab.
defineProps<{ tabs: TabItem<T>[]; label: string }>()
const model = defineModel<T>({ required: true })
const id = useId()
const buttons: HTMLButtonElement[] = []

function move(event: KeyboardEvent, index: number, tabs: TabItem<T>[]) {
  const last = tabs.length - 1
  const next = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: last }[event.key]
  if (next === undefined) return
  event.preventDefault()
  const target = next < 0 ? last : next > last ? 0 : next
  model.value = tabs[target].value
  buttons[target]?.focus()
}
</script>

<template>
  <div class="fabric-tabs">
    <div class="list" role="tablist" :aria-label="label">
      <button v-for="(t, i) in tabs" :id="`fabric-${id}-tab-${i}`" :key="String(t.value)" :ref="(el: unknown) => (buttons[i] = el as HTMLButtonElement)" type="button" role="tab"
        :aria-selected="model === t.value" :aria-controls="`fabric-${id}-panel`" :tabindex="model === t.value ? 0 : -1"
        @click="model = t.value" @keydown="move($event, i, tabs)">
        <FabricIcon v-if="t.icon" :name="t.icon" :size="16" />
        {{ t.label }}
        <FabricBadge v-if="t.badge != null" size="sm" :tone="model === t.value ? 'accent' : 'neutral'">{{ t.badge }}</FabricBadge>
      </button>
    </div>
    <div :id="`fabric-${id}-panel`" class="panel" role="tabpanel" tabindex="0"
      :aria-labelledby="`fabric-${id}-tab-${tabs.findIndex(t => t.value === model)}`">
      <slot name="panel" :value="model" />
    </div>
  </div>
</template>

<style scoped>
.list { display: flex; gap: 2px; border-bottom: 1px solid var(--fabric-line); overflow-x: auto; }
button {
  display: inline-flex; align-items: center; gap: 6px; padding: 8px 12px; margin-bottom: -1px; border: 0; border-bottom: 2px solid transparent;
  background: none; color: var(--fabric-muted); font: 500 var(--fabric-text-sm) var(--fabric-font); white-space: nowrap; cursor: pointer;
}
button:hover { color: var(--fabric-ink); }
button[aria-selected="true"] { color: var(--fabric-accent); border-bottom-color: var(--fabric-accent); font-weight: 600; }
button:focus-visible { outline: none; box-shadow: inset var(--fabric-focus); border-radius: 6px 6px 0 0; }
.panel { padding-top: var(--fabric-space-4); }
.panel:focus-visible { outline: none; }
</style>
