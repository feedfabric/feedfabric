<script setup lang="ts">
import { ref } from 'vue'
import FabricField from './FabricField.vue'
import FabricIcon, { type IconName } from './FabricIcon.vue'

// A one-line text field (v-model). icon goes inside on the left; clearable adds a button that empties it.
// Other attributes (placeholder, autocomplete, inputmode, min, max…) go on the <input> itself.
defineOptions({ inheritAttrs: false })
withDefaults(defineProps<{
  label?: string
  hint?: string
  error?: string | null
  type?: 'text' | 'search' | 'email' | 'url' | 'password' | 'number' | 'tel'
  icon?: IconName
  clearable?: boolean
  required?: boolean
  hideLabel?: boolean
  size?: 'sm' | 'md'
}>(), { type: 'text', size: 'md' })
const model = defineModel<string>({ default: '' })
const input = ref<HTMLInputElement>()

function clear() {
  model.value = ''
  input.value?.focus()
}

defineExpose({ focus: () => input.value?.focus() })
</script>

<template>
  <FabricField v-slot="{ id, describedBy, invalid }" :label="label" :hint="hint" :error="error" :required="required" :hide-label="hideLabel">
    <span class="fabric-input" :class="[size, { invalid, 'has-icon': icon }]">
      <FabricIcon v-if="icon" :name="icon" class="icon" :size="16" />
      <input :id="id" ref="input" v-model="model" v-bind="$attrs" :type="type" :required="required"
        :aria-invalid="invalid || undefined" :aria-describedby="describedBy">
      <button v-if="clearable && model" type="button" class="clear" aria-label="Clear" @click="clear">
        <FabricIcon name="x" :size="14" />
      </button>
    </span>
  </FabricField>
</template>

<style scoped>
.fabric-input {
  position: relative; display: flex; align-items: center; min-width: 0;
  border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-md); background: var(--fabric-surface);
  transition: border-color var(--fabric-fast), box-shadow var(--fabric-fast);
}
.fabric-input:hover { border-color: var(--fabric-line-strong); }
.fabric-input:focus-within { border-color: var(--fabric-accent); box-shadow: var(--fabric-focus); }
.invalid { border-color: var(--fabric-bad); }
input {
  flex: 1; min-width: 0; height: 34px; padding: 0 10px; border: 0; outline: 0; background: none;
  font: var(--fabric-text-md) var(--fabric-font); color: var(--fabric-ink);
}
.sm input { height: 28px; font-size: var(--fabric-text-sm); }
input::placeholder { color: var(--fabric-muted); }
.has-icon input { padding-left: 32px; }
.icon { position: absolute; left: 10px; color: var(--fabric-muted); pointer-events: none; }
.clear { display: grid; place-items: center; width: 24px; height: 24px; margin-right: 5px; border: 0; border-radius: 50%; background: none;
  color: var(--fabric-muted); cursor: pointer; }
.clear:hover { background: var(--fabric-line); color: var(--fabric-ink); }
.clear:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
</style>
