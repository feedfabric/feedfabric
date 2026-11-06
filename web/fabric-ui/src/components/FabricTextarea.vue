<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import FabricField from './FabricField.vue'

// Multi-line text (v-model) that grows with what's typed, up to maxRows. With maxlength it shows how much is left.
defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{
  label?: string
  hint?: string
  error?: string | null
  rows?: number
  maxRows?: number
  maxlength?: number
  required?: boolean
  hideLabel?: boolean
}>(), { rows: 3, maxRows: 12 })
const model = defineModel<string>({ default: '' })
const area = ref<HTMLTextAreaElement>()

function resize() {
  const el = area.value
  if (!el) return
  el.style.height = 'auto'
  const line = parseFloat(getComputedStyle(el).lineHeight) || 22
  const max = line * props.maxRows + 16
  el.style.height = `${Math.min(el.scrollHeight + 2, max)}px`
  el.style.overflowY = el.scrollHeight + 2 > max ? 'auto' : 'hidden'
}

onMounted(resize)
watch(model, () => nextTick(resize))

defineExpose({ focus: () => area.value?.focus() })
</script>

<template>
  <FabricField :label="label" :hint="hint" :error="error" :required="required" :hide-label="hideLabel">
    <template v-if="maxlength" #aside>
      <span :class="{ near: model.length > maxlength * 0.9 }">{{ model.length }} / {{ maxlength }}</span>
    </template>
    <template #default="{ id, describedBy, invalid }">
      <textarea :id="id" ref="area" v-model="model" v-bind="$attrs" class="fabric-textarea" :class="{ invalid }" :rows="rows"
        :maxlength="maxlength" :required="required" :aria-invalid="invalid || undefined" :aria-describedby="describedBy" />
    </template>
  </FabricField>
</template>

<style scoped>
.fabric-textarea {
  box-sizing: border-box; width: 100%; min-width: 0; padding: 7px 10px; resize: none; overflow-y: hidden;
  border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-md); background: var(--fabric-surface);
  font: var(--fabric-text-md)/1.45 var(--fabric-font); color: var(--fabric-ink);
  transition: border-color var(--fabric-fast), box-shadow var(--fabric-fast);
}
.fabric-textarea:hover { border-color: var(--fabric-line-strong); }
.fabric-textarea:focus { outline: none; border-color: var(--fabric-accent); box-shadow: var(--fabric-focus); }
.fabric-textarea::placeholder { color: var(--fabric-muted); }
.invalid { border-color: var(--fabric-bad); }
.near { color: var(--fabric-warn); font-weight: 600; }
</style>
