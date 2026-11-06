<script setup lang="ts" generic="T extends string | number">
import type { SegmentOption } from '../types'
import FabricIcon from './FabricIcon.vue'

// One choice from a few, side by side (v-model): a radio group, so Tab reaches the chosen one and the arrow keys
// move between them, Home and End jump to the ends.

defineProps<{ options: SegmentOption<T>[]; label: string; size?: 'sm' | 'md' }>()
const model = defineModel<T>({ required: true })
const buttons: HTMLButtonElement[] = []

function move(event: KeyboardEvent, index: number, options: SegmentOption<T>[]) {
  const last = options.length - 1
  const next = { ArrowRight: index + 1, ArrowDown: index + 1, ArrowLeft: index - 1, ArrowUp: index - 1, Home: 0, End: last }[event.key]
  if (next === undefined) return
  event.preventDefault()
  const target = next < 0 ? last : next > last ? 0 : next
  model.value = options[target].value
  buttons[target]?.focus()
}
</script>

<template>
  <div class="fabric-segmented" :class="size ?? 'md'" role="radiogroup" :aria-label="label">
    <button v-for="(o, i) in options" :key="String(o.value)" :ref="(el: unknown) => (buttons[i] = el as HTMLButtonElement)" type="button" role="radio" :aria-checked="model === o.value"
      :tabindex="model === o.value ? 0 : -1" :aria-label="o.iconOnly ? o.label : undefined" :title="o.iconOnly ? o.label : undefined"
      @click="model = o.value" @keydown="move($event, i, options)">
      <FabricIcon v-if="o.icon" :name="o.icon" :size="14" />
      <span v-if="!o.iconOnly">{{ o.label }}</span>
    </button>
  </div>
</template>

<style scoped>
.fabric-segmented {
  display: inline-flex; gap: 2px; padding: 2px; border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-full);
  background: var(--fabric-surface); max-width: 100%; overflow-x: auto;
}
button {
  display: inline-flex; align-items: center; gap: 5px; height: 28px; padding: 0 12px; border: 0; border-radius: var(--fabric-radius-full);
  background: none; color: var(--fabric-muted); font: 500 var(--fabric-text-sm) var(--fabric-font); white-space: nowrap; cursor: pointer;
  transition: background var(--fabric-fast), color var(--fabric-fast);
}
.sm button { height: 24px; padding: 0 10px; font-size: var(--fabric-text-xs); }
button:hover { color: var(--fabric-ink); }
button[aria-checked="true"] { background: var(--fabric-accent-soft); color: var(--fabric-accent); font-weight: 600; }
button:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
</style>
