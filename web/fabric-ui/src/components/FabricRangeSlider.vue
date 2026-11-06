<script setup lang="ts">
import { computed, useId } from 'vue'

// A number on a scale (v-model), with its value shown beside the label. format turns the number into words for
// both the display and screen readers ("×1.5", "6 hours").
const props = withDefaults(defineProps<{
  label: string
  min?: number
  max?: number
  step?: number
  hint?: string
  format?: (value: number) => string
  disabled?: boolean
}>(), { min: 0, max: 100, step: 1 })
const model = defineModel<number>({ required: true })
const id = useId()

const shown = computed(() => (props.format ? props.format(model.value) : String(model.value)))
const fill = computed(() => `${((model.value - props.min) / (props.max - props.min || 1)) * 100}%`)
</script>

<template>
  <div class="fabric-range" :class="{ disabled }">
    <div class="row">
      <label :for="`fabric-${id}`">{{ label }}</label>
      <output :for="`fabric-${id}`">{{ shown }}</output>
    </div>
    <input :id="`fabric-${id}`" v-model.number="model" type="range" :min="min" :max="max" :step="step" :disabled="disabled"
      :aria-valuetext="shown" :aria-describedby="hint ? `fabric-${id}-hint` : undefined" :style="{ '--fill': fill }">
    <p v-if="hint" :id="`fabric-${id}-hint`" class="hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.fabric-range { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.row { display: flex; justify-content: space-between; gap: 8px; font-size: var(--fabric-text-sm); }
label { font-weight: 600; }
output { color: var(--fabric-accent); font-weight: 600; font-variant-numeric: tabular-nums; }
.hint { margin: 0; font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
input {
  appearance: none; width: 100%; height: 20px; margin: 0; background: none; cursor: pointer;
}
input::-webkit-slider-runnable-track {
  height: 6px; border-radius: 99px;
  background: linear-gradient(to right, var(--fabric-accent) var(--fill), var(--fabric-line) var(--fill));
}
input::-moz-range-track { height: 6px; border-radius: 99px; background: var(--fabric-line); }
input::-moz-range-progress { height: 6px; border-radius: 99px; background: var(--fabric-accent); }
input::-webkit-slider-thumb {
  appearance: none; width: 18px; height: 18px; margin-top: -6px; border-radius: 50%; background: var(--fabric-surface);
  border: 2px solid var(--fabric-accent); box-shadow: var(--fabric-shadow-sm);
}
input::-moz-range-thumb { width: 14px; height: 14px; border-radius: 50%; background: var(--fabric-surface); border: 2px solid var(--fabric-accent); }
input:focus-visible { outline: none; }
input:focus-visible::-webkit-slider-thumb { box-shadow: var(--fabric-focus); }
input:focus-visible::-moz-range-thumb { box-shadow: var(--fabric-focus); }
.disabled { opacity: .5; }
.disabled input { cursor: default; }
</style>
