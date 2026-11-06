<script setup lang="ts">
// The label, hint and error around one form control. The control gets the ids through the default slot, so the
// label names it and the hint and error are read out with it:
//   <FabricField label="Name" v-slot="{ id, describedBy, invalid }"><input :id="id" :aria-describedby="describedBy" ...></FabricField>
import { computed, useId } from 'vue'

const props = defineProps<{ label?: string; hint?: string; error?: string | null; required?: boolean; hideLabel?: boolean; id?: string }>()

const autoId = useId()
const controlId = computed(() => props.id ?? `fabric-${autoId}`)
const hintId = computed(() => `${controlId.value}-hint`)
const errorId = computed(() => `${controlId.value}-error`)
const describedBy = computed(() => [props.error ? errorId.value : null, props.hint ? hintId.value : null].filter(Boolean).join(' ') || undefined)
</script>

<template>
  <div class="fabric-field" :class="{ invalid: !!error }">
    <div v-if="label || $slots.aside" class="label-row">
      <label v-if="label" :for="controlId" :class="{ 'fabric-sr-only': hideLabel }">
        {{ label }}<span v-if="required" class="required" aria-hidden="true"> *</span>
      </label>
      <span v-if="$slots.aside" class="aside"><slot name="aside" /></span>
    </div>
    <slot :id="controlId" :described-by="describedBy" :invalid="!!error" />
    <p v-if="error" :id="errorId" class="error" role="alert">{{ error }}</p>
    <p v-if="hint" :id="hintId" class="hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.fabric-field { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.label-row { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
label { font-size: var(--fabric-text-sm); font-weight: 600; color: var(--fabric-ink); }
.required { color: var(--fabric-bad); }
.aside { font-size: var(--fabric-text-xs); color: var(--fabric-muted); font-variant-numeric: tabular-nums; }
.hint, .error { margin: 0; font-size: var(--fabric-text-xs); }
.hint { color: var(--fabric-muted); }
.error { color: var(--fabric-bad); }
</style>
