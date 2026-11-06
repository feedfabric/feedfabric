<script setup lang="ts" generic="T extends string | number">
import type { SelectGroup, SelectOption } from '../types'
import FabricField from './FabricField.vue'
import FabricIcon from './FabricIcon.vue'

// A native select (v-model), so phones show their own picker and keyboards work as people expect.
// options can be grouped: { label, options: [...] } becomes an <optgroup>.

defineOptions({ inheritAttrs: false })
withDefaults(defineProps<{
  options: (SelectOption<T> | SelectGroup<T>)[]
  label?: string
  hint?: string
  error?: string | null
  placeholder?: string
  required?: boolean
  hideLabel?: boolean
  size?: 'sm' | 'md'
}>(), { size: 'md' })
const model = defineModel<T>()

const isGroup = (o: SelectOption<T> | SelectGroup<T>): o is SelectGroup<T> => 'options' in o
</script>

<template>
  <FabricField v-slot="{ id, describedBy, invalid }" :label="label" :hint="hint" :error="error" :required="required" :hide-label="hideLabel">
    <span class="fabric-select" :class="[size, { invalid }]">
      <select :id="id" v-model="model" v-bind="$attrs" :required="required" :aria-invalid="invalid || undefined" :aria-describedby="describedBy">
        <option v-if="placeholder" :value="undefined" disabled>{{ placeholder }}</option>
        <template v-for="o in options" :key="isGroup(o) ? `g:${o.label}` : String(o.value)">
          <optgroup v-if="isGroup(o)" :label="o.label">
            <option v-for="c in o.options" :key="String(c.value)" :value="c.value" :disabled="c.disabled">{{ c.label }}</option>
          </optgroup>
          <option v-else :value="o.value" :disabled="o.disabled">{{ o.label }}</option>
        </template>
      </select>
      <FabricIcon name="chevron-down" class="chevron" :size="16" />
    </span>
  </FabricField>
</template>

<style scoped>
.fabric-select { position: relative; display: flex; min-width: 0; }
select {
  appearance: none; box-sizing: border-box; width: 100%; min-width: 0; height: 34px; padding: 0 32px 0 10px;
  border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-md); background: var(--fabric-surface);
  font: var(--fabric-text-md) var(--fabric-font); color: var(--fabric-ink); cursor: pointer;
  transition: border-color var(--fabric-fast), box-shadow var(--fabric-fast);
}
.sm select { height: 28px; font-size: var(--fabric-text-sm); }
select:hover { border-color: var(--fabric-line-strong); }
select:focus { outline: none; border-color: var(--fabric-accent); box-shadow: var(--fabric-focus); }
.invalid select { border-color: var(--fabric-bad); }
.chevron { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); color: var(--fabric-muted); pointer-events: none; }
</style>
