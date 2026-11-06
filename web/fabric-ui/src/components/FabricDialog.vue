<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import FabricButton from './FabricButton.vue'

// A modal (v-model:open) on the native <dialog>: the browser keeps focus inside, makes the rest of the page inert
// and closes it on Escape. Focus goes to the first field (or autofocus element) when it opens and back to whatever
// opened it when it closes. Clicking the backdrop closes it unless persistent.
const props = withDefaults(defineProps<{ title: string; description?: string; size?: 'sm' | 'md' | 'lg'; persistent?: boolean }>(), { size: 'md' })
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ close: [] }>()

const id = useId()
const dialog = ref<HTMLDialogElement>()
let opener: HTMLElement | null = null

async function sync(isOpen: boolean) {
  const el = dialog.value
  if (!el) return
  if (isOpen && !el.open) {
    opener = document.activeElement as HTMLElement | null
    if (typeof el.showModal === 'function') el.showModal()
    else el.setAttribute('open', '')
    await nextTick()
    const target = el.querySelector<HTMLElement>('[autofocus], input, textarea, select') ?? el.querySelector<HTMLElement>('.body button, .foot button')
    target?.focus()
  } else if (!isOpen && el.open) {
    if (typeof el.close === 'function') el.close()
    else el.removeAttribute('open')
  }
}

watch(open, sync, { flush: 'post' })
// Mounted already open, as with v-if and v-model:open on the same state
onMounted(() => open.value && sync(true))

function onClose() {
  if (open.value) open.value = false
  emit('close')
  opener?.focus?.()
  opener = null
}

function onCancel(event: Event) {
  if (props.persistent) event.preventDefault()
}

function onBackdrop(event: MouseEvent) {
  if (!props.persistent && event.target === dialog.value) open.value = false
}

onBeforeUnmount(() => dialog.value?.open && dialog.value.close?.())
</script>

<template>
  <dialog ref="dialog" class="fabric-dialog" :class="size" :aria-labelledby="`fabric-${id}-title`"
    :aria-describedby="description ? `fabric-${id}-desc` : undefined" @close="onClose" @cancel="onCancel" @click="onBackdrop">
    <div class="panel">
      <header class="head">
        <div>
          <h2 :id="`fabric-${id}-title`">{{ title }}</h2>
          <p v-if="description" :id="`fabric-${id}-desc`" class="description">{{ description }}</p>
        </div>
        <FabricButton v-if="!persistent" variant="ghost" size="sm" icon="x" label="Close" @click="open = false" />
      </header>
      <div class="body"><slot /></div>
      <footer v-if="$slots.footer" class="foot"><slot name="footer" /></footer>
    </div>
  </dialog>
</template>

<style scoped>
.fabric-dialog {
  box-sizing: border-box; width: min(var(--w), calc(100vw - 32px)); max-height: calc(100dvh - 48px); padding: 0; border: 1px solid var(--fabric-line);
  border-radius: var(--fabric-radius-lg); background: var(--fabric-surface-raised); color: var(--fabric-ink); box-shadow: var(--fabric-shadow-md);
}
.sm { --w: 400px; }
.md { --w: 560px; }
.lg { --w: 820px; }
.fabric-dialog[open] { animation: fabric-rise var(--fabric-slow) var(--fabric-ease); }
.fabric-dialog::backdrop { background: var(--fabric-scrim); }
.panel { display: flex; flex-direction: column; max-height: inherit; }
.head { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; padding: 16px 16px 0 20px; }
h2 { margin: 0; font-size: var(--fabric-text-lg); }
.description { margin: 4px 0 0; color: var(--fabric-muted); font-size: var(--fabric-text-sm); }
.body { padding: 14px 20px 20px; overflow-y: auto; }
.foot { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 8px; padding: 12px 20px; border-top: 1px solid var(--fabric-line); }
</style>
