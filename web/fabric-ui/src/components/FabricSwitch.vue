<script setup lang="ts">
import { useId } from 'vue'

// An on/off setting (v-model) that takes effect straight away. The whole row, label and description included,
// toggles it; screen readers hear a switch with its state.
defineProps<{ label: string; description?: string; disabled?: boolean }>()
const model = defineModel<boolean>({ default: false })
const id = useId()
</script>

<template>
  <div class="fabric-switch" :class="{ disabled }">
    <button :id="`fabric-${id}`" type="button" role="switch" class="track" :aria-checked="model" :disabled="disabled"
      :aria-describedby="description ? `fabric-${id}-desc` : undefined" @click="model = !model">
      <span class="thumb" />
    </button>
    <div class="text">
      <label :for="`fabric-${id}`">{{ label }}</label>
      <p v-if="description" :id="`fabric-${id}-desc`">{{ description }}</p>
    </div>
  </div>
</template>

<style scoped>
.fabric-switch { display: flex; align-items: flex-start; gap: 10px; }
.track {
  flex: none; position: relative; width: 36px; height: 20px; margin-top: 1px; padding: 0; border-radius: 99px;
  border: 1px solid var(--fabric-line-strong); background: var(--fabric-line); cursor: pointer;
  transition: background var(--fabric-fast), border-color var(--fabric-fast);
}
.thumb {
  position: absolute; top: 2px; left: 2px; width: 14px; height: 14px; border-radius: 50%; background: #fff;
  box-shadow: 0 1px 2px rgb(0 0 0 / .25); transition: transform var(--fabric-slow) var(--fabric-ease);
}
.track[aria-checked="true"] { background: var(--fabric-accent); border-color: var(--fabric-accent); }
.track[aria-checked="true"] .thumb { transform: translateX(16px); }
.track:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
.text { min-width: 0; }
label { font-size: var(--fabric-text-sm); font-weight: 600; cursor: pointer; }
p { margin: 1px 0 0; font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.disabled { opacity: .5; }
.disabled label, .disabled .track { cursor: default; }
</style>
