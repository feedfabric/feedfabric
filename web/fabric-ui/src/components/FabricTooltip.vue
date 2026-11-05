<script setup lang="ts">
import { ref, useId } from 'vue'

// A short explanation that appears on hover or keyboard focus, and goes with Escape. The wrapped element is
// described by it, so screen readers read it too. Only for extra detail: never put anything essential in a tooltip.
withDefaults(defineProps<{ text: string; placement?: 'top' | 'bottom' }>(), { placement: 'top' })
const id = useId()
const shown = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

function show() {
  clearTimeout(timer)
  timer = setTimeout(() => (shown.value = true), 250)
}

function hide() {
  clearTimeout(timer)
  shown.value = false
}
</script>

<template>
  <span class="fabric-tooltip" :aria-describedby="`fabric-${id}`" @mouseenter="show" @mouseleave="hide" @focusin="show" @focusout="hide"
    @keydown.esc="hide">
    <slot />
    <span :id="`fabric-${id}`" role="tooltip" class="bubble" :class="[placement, { shown }]">{{ text }}</span>
  </span>
</template>

<style scoped>
.fabric-tooltip { position: relative; display: inline-flex; }
.bubble {
  position: absolute; left: 50%; z-index: 50; width: max-content; max-width: 260px; padding: 5px 8px; border-radius: var(--fabric-radius-sm);
  background: var(--fabric-ink); color: var(--fabric-bg); font: var(--fabric-text-xs)/1.4 var(--fabric-font); text-align: center;
  pointer-events: none; opacity: 0; visibility: hidden; transform: translate(-50%, 4px);
  transition: opacity var(--fabric-fast), transform var(--fabric-fast), visibility var(--fabric-fast);
}
.top { bottom: calc(100% + 6px); }
.bottom { top: calc(100% + 6px); transform: translate(-50%, -4px); }
.shown { opacity: 1; visibility: visible; transform: translate(-50%, 0); }
</style>
