<script setup lang="ts">
import { computed } from 'vue'

// How far along something is (0 to 100), or a moving bar when there's no telling (value null)
const props = withDefaults(defineProps<{ value?: number | null; label: string; tone?: 'accent' | 'good' | 'warn' | 'bad'; showValue?: boolean }>(),
  { value: null, tone: 'accent' })

const clamped = computed(() => (props.value == null ? null : Math.max(0, Math.min(100, props.value))))
</script>

<template>
  <div class="fabric-progress" :class="tone">
    <div v-if="showValue" class="row"><span>{{ label }}</span><span v-if="clamped != null" class="value">{{ Math.round(clamped) }}%</span></div>
    <div class="track" role="progressbar" :aria-label="label" aria-valuemin="0" aria-valuemax="100" :aria-valuenow="clamped ?? undefined">
      <div class="bar" :class="{ indeterminate: clamped == null }" :style="clamped == null ? undefined : { width: `${clamped}%` }" />
    </div>
  </div>
</template>

<style scoped>
.fabric-progress { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.row { display: flex; justify-content: space-between; font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.value { font-variant-numeric: tabular-nums; font-weight: 600; color: var(--fabric-ink); }
.track { position: relative; height: 6px; border-radius: 99px; background: var(--fabric-line); overflow: hidden; }
.bar { height: 100%; border-radius: inherit; background: var(--strong); transition: width var(--fabric-slow) var(--fabric-ease); }
.indeterminate { width: 40%; animation: fabric-indeterminate 1.2s var(--fabric-ease) infinite; }
.accent { --strong: var(--fabric-accent); }
.good { --strong: var(--fabric-good); }
.warn { --strong: var(--fabric-warn); }
.bad { --strong: var(--fabric-bad); }
@media (prefers-reduced-motion: reduce) { .indeterminate { animation-duration: 3s; } }
</style>
