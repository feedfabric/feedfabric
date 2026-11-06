<script setup lang="ts">
import FabricIcon from './FabricIcon.vue'

// A tag, filter or choice. selectable makes it a toggle button (v-model:selected); removable adds a remove button,
// and Backspace or Delete on it removes it too.
const props = withDefaults(defineProps<{ selectable?: boolean; removable?: boolean; removeLabel?: string }>(), { removeLabel: 'Remove' })
const selected = defineModel<boolean>('selected', { default: false })
const emit = defineEmits<{ remove: [] }>()

function toggle() {
  if (props.selectable) selected.value = !selected.value
}

function onKey(event: KeyboardEvent) {
  if (props.removable && (event.key === 'Backspace' || event.key === 'Delete')) {
    event.preventDefault()
    emit('remove')
  }
}
</script>

<template>
  <span class="fabric-chip" :class="{ selected, selectable }">
    <button v-if="selectable" type="button" class="face" :aria-pressed="selected" @click="toggle" @keydown="onKey">
      <FabricIcon v-if="selected" name="check" :size="13" /><slot />
    </button>
    <span v-else class="face" :tabindex="removable ? 0 : undefined" @keydown="onKey"><slot /></span>
    <button v-if="removable" type="button" class="remove" :aria-label="removeLabel" @click="emit('remove')">
      <FabricIcon name="x" :size="12" />
    </button>
  </span>
</template>

<style scoped>
.fabric-chip {
  display: inline-flex; align-items: center; border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-full);
  background: var(--fabric-surface); font: 500 var(--fabric-text-sm)/1.2 var(--fabric-font); color: var(--fabric-ink); max-width: 100%;
}
.face { display: inline-flex; align-items: center; gap: 4px; padding: 4px 10px; border: 0; background: none; color: inherit; font: inherit;
  border-radius: inherit; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.selectable .face { cursor: pointer; }
.selectable:hover { border-color: var(--fabric-line-strong); }
.selected { background: var(--fabric-accent-soft); border-color: var(--fabric-accent); color: var(--fabric-accent); }
.remove { display: grid; place-items: center; width: 20px; height: 20px; margin-right: 4px; margin-left: -4px; border: 0; border-radius: 50%;
  background: none; color: var(--fabric-muted); cursor: pointer; }
.remove:hover { background: var(--fabric-bad-soft); color: var(--fabric-bad); }
.face:focus-visible, .remove:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
</style>
