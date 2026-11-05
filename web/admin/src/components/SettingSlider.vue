<script setup lang="ts">
// One numeric setting: its name and what it does, a slider, and the value (typed or dragged)
defineProps<{ label: string; hint?: string; min: number; max: number; step?: number; unit?: string; error?: string | null; disabled?: boolean }>()
const value = defineModel<number>({ required: true })
</script>

<template>
  <label class="setting" :class="{ disabled }">
    <span class="text">
      <span class="label">{{ label }}</span>
      <span v-if="hint" class="hint">{{ hint }}</span>
    </span>
    <input v-model.number="value" type="range" :min="min" :max="max" :step="step ?? 1" :disabled="disabled" />
    <span class="value">
      <input v-model.number="value" type="number" :min="min" :max="max" :step="step ?? 1" :disabled="disabled" :aria-label="label" />
      <span v-if="unit" class="unit">{{ unit }}</span>
    </span>
    <span v-if="error" class="error">{{ error }}</span>
  </label>
</template>

<style scoped>
.setting { display: grid; grid-template-columns: minmax(0, 1fr) minmax(90px, 160px) 92px; gap: 2px 12px; align-items: center; padding: 6px 0; }
.setting.disabled { opacity: .5; }
.text { display: flex; flex-direction: column; min-width: 0; }
.label { font-size: 14px; }
.hint { font-size: 12px; color: var(--muted); }
input[type="range"] { width: 100%; accent-color: var(--accent); }
.value { display: flex; align-items: center; gap: 4px; }
.value input { width: 64px; padding: 3px 6px; font-variant-numeric: tabular-nums; text-align: right; }
.unit { font-size: 12px; color: var(--muted); }
.error { grid-column: 1 / -1; }
@media (max-width: 520px) { .setting { grid-template-columns: 1fr 92px; } input[type="range"] { grid-column: 1 / -1; grid-row: 2; } }
</style>
