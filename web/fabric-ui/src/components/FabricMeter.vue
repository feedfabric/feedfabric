<script setup lang="ts">
import { computed } from 'vue'

// A score from 0 to 1 against the line where something happens, like a classifier's score and the threshold it
// acts at. At or past the threshold it turns the threshold's tone; within warnWithin of it, it warns. format, when
// given, writes both the value and the threshold, so they're in the same units.
const props = withDefaults(defineProps<{
  value: number
  label: string
  threshold?: number | null
  thresholdLabel?: string
  tone?: 'bad' | 'warn' | 'good'
  warnWithin?: number
  format?: (value: number) => string
}>(), { threshold: null, tone: 'bad', warnWithin: 0.15 })

const percent = (v: number) => `${Math.max(0, Math.min(1, v)) * 100}%`
const write = (v: number) => (props.format ? props.format(v) : String(Number(v.toFixed(4))))
const shown = computed(() => write(props.value))
// Near either end the threshold's label sits on its inner side, so it never runs into the 0 or 1
const markSide = computed(() => (props.threshold == null ? 'middle' : props.threshold < 0.15 ? 'low' : props.threshold > 0.85 ? 'high' : 'middle'))
const state = computed(() => {
  if (props.threshold == null) return 'accent'
  if (props.value >= props.threshold) return props.tone
  return props.value >= props.threshold - props.warnWithin ? 'warn' : 'good'
})
</script>

<template>
  <div class="fabric-meter" :class="state">
    <div class="row">
      <span class="label">{{ label }}</span>
      <span class="value">{{ shown }}</span>
    </div>
    <div class="track" role="meter" :aria-label="label" aria-valuemin="0" aria-valuemax="1" :aria-valuenow="value" :aria-valuetext="shown">
      <div class="bar" :style="{ width: percent(value) }" />
      <div v-if="threshold != null" class="threshold" :style="{ left: percent(threshold) }" />
    </div>
    <div v-if="threshold != null" class="scale">
      <span :class="{ covered: markSide === 'low' }">0</span>
      <span class="mark" :class="markSide" :style="{ left: percent(threshold) }">{{ thresholdLabel ?? `acts at ${write(threshold)}` }}</span>
      <span :class="{ covered: markSide === 'high' }">1</span>
    </div>
  </div>
</template>

<style scoped>
.fabric-meter { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.row { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
.label { font-size: var(--fabric-text-sm); font-weight: 600; }
.value { font-size: var(--fabric-text-lg); font-weight: 650; font-variant-numeric: tabular-nums; color: var(--strong); }
.track { position: relative; height: 10px; border-radius: 99px; background: var(--fabric-line); }
.bar { height: 100%; border-radius: inherit; background: var(--strong); transition: width var(--fabric-slow) var(--fabric-ease); }
.threshold { position: absolute; top: -3px; bottom: -3px; width: 2px; margin-left: -1px; background: var(--fabric-ink); border-radius: 1px; }
.scale { position: relative; display: flex; justify-content: space-between; height: 16px; font-size: 11px; color: var(--fabric-muted); }
.mark { position: absolute; transform: translateX(-50%); white-space: nowrap; color: var(--fabric-ink); }
.mark.low { transform: translateX(-2px); }
.mark.high { transform: translateX(calc(-100% + 2px)); }
.covered { visibility: hidden; }
.accent { --strong: var(--fabric-accent); }
.good { --strong: var(--fabric-good); }
.warn { --strong: var(--fabric-warn); }
.bad { --strong: var(--fabric-bad); }
</style>
