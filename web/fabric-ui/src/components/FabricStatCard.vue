<script setup lang="ts">
import { computed } from 'vue'
import FabricIcon from './FabricIcon.vue'
import FabricSkeleton from './FabricSkeleton.vue'

// One number that matters, with what it's of, and optionally how it changed. delta is a percentage; whether up is
// good depends on the number, so higherIsBetter says which way to colour it.
const props = withDefaults(defineProps<{
  label: string
  value?: string | number | null
  delta?: number | null
  deltaLabel?: string
  hint?: string
  higherIsBetter?: boolean
  loading?: boolean
}>(), { higherIsBetter: true })

const trend = computed(() => {
  if (props.delta == null || props.delta === 0) return 'flat'
  return props.delta > 0 === props.higherIsBetter ? 'good' : 'bad'
})
const formatted = computed(() => (typeof props.value === 'number' ? props.value.toLocaleString() : props.value ?? '–'))
</script>

<template>
  <div class="fabric-stat">
    <span class="label">{{ label }}</span>
    <FabricSkeleton v-if="loading" width="50%" height="28px" />
    <span v-else class="value">{{ formatted }}</span>
    <span v-if="!loading && delta != null" class="delta" :class="trend">
      <FabricIcon v-if="delta !== 0" :name="delta > 0 ? 'chevron-up' : 'chevron-down'" :size="14" />
      {{ Math.abs(delta).toLocaleString(undefined, { maximumFractionDigits: 1 }) }}%
      <span v-if="deltaLabel" class="muted">{{ deltaLabel }}</span>
    </span>
    <span v-if="hint" class="hint">{{ hint }}</span>
  </div>
</template>

<style scoped>
.fabric-stat {
  display: flex; flex-direction: column; gap: 4px; min-width: 0; padding: var(--fabric-space-4);
  background: var(--fabric-surface); border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-lg); box-shadow: var(--fabric-shadow-sm);
}
.label { font-size: var(--fabric-text-xs); font-weight: 600; text-transform: uppercase; letter-spacing: .06em; color: var(--fabric-muted); }
.value { font-size: var(--fabric-text-2xl); font-weight: 650; letter-spacing: -.02em; line-height: 1.15; font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
.delta { display: inline-flex; align-items: center; gap: 2px; font-size: var(--fabric-text-sm); font-weight: 600; }
.delta.good { color: var(--fabric-good); }
.delta.bad { color: var(--fabric-bad); }
.delta.flat { color: var(--fabric-muted); }
.muted { margin-left: 4px; font-weight: 400; color: var(--fabric-muted); }
.hint { font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
</style>
