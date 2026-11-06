<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import type { FileRejection } from '../types'
import FabricIcon from './FabricIcon.vue'

// Choose files by dropping them on the zone, browsing, or pasting images. Files are checked against accept,
// maxBytes and maxFiles here; those that pass are emitted as files, the rest as rejected with the reason, so the
// page can say why. The server checks again: this is for quick feedback, not security.
const props = withDefaults(defineProps<{
  label?: string
  hint?: string
  accept?: string
  multiple?: boolean
  maxBytes?: number
  maxFiles?: number
  disabled?: boolean
  compact?: boolean
}>(), { label: 'Add files', multiple: true, maxFiles: 10 })
const emit = defineEmits<{ files: [files: File[]]; rejected: [rejections: FileRejection[]] }>()

const id = useId()
const input = ref<HTMLInputElement>()
const dragging = ref(false)
let depth = 0

const acceptList = computed(() => (props.accept ?? '').split(',').map(a => a.trim().toLowerCase()).filter(Boolean))

function allowed(file: File) {
  if (!acceptList.value.length) return true
  const type = file.type.toLowerCase()
  const name = file.name.toLowerCase()
  return acceptList.value.some(a => (a.startsWith('.') ? name.endsWith(a) : a.endsWith('/*') ? type.startsWith(a.slice(0, -1)) : type === a))
}

function take(list: FileList | File[] | null | undefined) {
  if (!list || props.disabled) return
  const accepted: File[] = []
  const rejected: FileRejection[] = []
  const limit = props.multiple ? props.maxFiles : 1
  for (const file of Array.from(list)) {
    if (!allowed(file)) rejected.push({ file, reason: 'type' })
    else if (props.maxBytes && file.size > props.maxBytes) rejected.push({ file, reason: 'size' })
    else if (accepted.length >= limit) rejected.push({ file, reason: 'count' })
    else accepted.push(file)
  }
  if (accepted.length) emit('files', accepted)
  if (rejected.length) emit('rejected', rejected)
}

function onChange(event: Event) {
  const el = event.target as HTMLInputElement
  take(el.files)
  el.value = ''
}

function onDrop(event: DragEvent) {
  depth = 0
  dragging.value = false
  take(event.dataTransfer?.files)
}

function onEnter() {
  depth++
  dragging.value = true
}

function onLeave() {
  depth = Math.max(0, depth - 1)
  if (depth === 0) dragging.value = false
}

function onPaste(event: ClipboardEvent) {
  const files = Array.from(event.clipboardData?.files ?? [])
  if (files.length) {
    event.preventDefault()
    take(files)
  }
}

// 20 MB, 1.5 MB, 300 KB: as exact as it needs to be to be true
function size(bytes: number) {
  const [value, unit] = bytes >= 1024 * 1024 ? [bytes / 1024 / 1024, 'MB'] : [bytes / 1024, 'KB']
  return `${Number.isInteger(value) ? value : value.toFixed(1)} ${unit}`
}

const sizeText = computed(() => (props.maxBytes ? `up to ${size(props.maxBytes)}${props.multiple ? ' each' : ''}` : ''))

defineExpose({ browse: () => input.value?.click() })
</script>

<template>
  <div class="fabric-upload" :class="{ dragging, disabled, compact }" @dragenter.prevent="onEnter" @dragover.prevent @dragleave="onLeave"
    @drop.prevent="onDrop" @paste="onPaste">
    <input :id="`fabric-${id}`" ref="input" type="file" class="fabric-sr-only" :accept="accept" :multiple="multiple" :disabled="disabled"
      :aria-describedby="`fabric-${id}-hint`" @change="onChange">
    <label :for="`fabric-${id}`" class="zone">
      <span class="icon"><FabricIcon name="upload" :size="compact ? 16 : 20" /></span>
      <span class="text">
        <strong>{{ label }}</strong>
        <span v-if="!compact" class="sub">Drop {{ multiple ? 'them' : 'it' }} here, paste, or browse</span>
      </span>
    </label>
    <p :id="`fabric-${id}-hint`" class="hint">
      <slot name="hint">{{ hint }}<template v-if="hint && sizeText"> · </template>{{ sizeText }}</slot>
    </p>
  </div>
</template>

<style scoped>
.fabric-upload { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.zone {
  display: flex; align-items: center; gap: 12px; padding: 16px; cursor: pointer;
  border: 1.5px dashed var(--fabric-line-strong); border-radius: var(--fabric-radius-lg); background: var(--fabric-sunken);
  transition: border-color var(--fabric-fast), background var(--fabric-fast);
}
.compact .zone { padding: 8px 12px; border-radius: var(--fabric-radius-md); }
.zone:hover, .dragging .zone { border-color: var(--fabric-accent); background: var(--fabric-accent-soft); }
input:focus-visible + .zone { border-color: var(--fabric-accent); box-shadow: var(--fabric-focus); }
.icon { display: grid; place-items: center; width: 38px; height: 38px; flex: none; border-radius: 50%; background: var(--fabric-accent-soft); color: var(--fabric-accent); }
.compact .icon { width: 28px; height: 28px; }
.text { display: flex; flex-direction: column; min-width: 0; font-size: var(--fabric-text-sm); }
.sub { color: var(--fabric-muted); }
.hint { margin: 0; font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.hint:empty { display: none; }
.disabled { opacity: .5; }
.disabled .zone { cursor: default; }
</style>
