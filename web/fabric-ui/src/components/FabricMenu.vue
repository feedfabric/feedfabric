<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, useId } from 'vue'
import type { MenuAction } from '../types'
import FabricButton from './FabricButton.vue'
import FabricIcon from './FabricIcon.vue'

// A button that opens a list of actions. Follows the ARIA menu button pattern: Enter, Space or ArrowDown opens it
// on the first item, ArrowUp on the last; arrows move, Home and End jump, typing a letter jumps to the next item
// starting with it, Escape or Tab closes it, and focus goes back to the button.
const props = withDefaults(defineProps<{ items: MenuAction[]; label: string; icon?: 'more' | 'settings' | 'filter'; align?: 'start' | 'end' }>(),
  { icon: 'more', align: 'end' })

const id = useId()
const open = ref(false)
const root = ref<HTMLElement>()
const trigger = ref<HTMLElement>()
const itemEls: HTMLButtonElement[] = []

const enabled = () => props.items.map((item, i) => (item.disabled ? -1 : i)).filter(i => i >= 0)

async function show(focus: 'first' | 'last' = 'first') {
  open.value = true
  document.addEventListener('pointerdown', outside, true)
  await nextTick()
  const indices = enabled()
  itemEls[focus === 'first' ? indices[0] : indices[indices.length - 1]]?.focus()
}

function hide(returnFocus = true) {
  if (!open.value) return
  open.value = false
  document.removeEventListener('pointerdown', outside, true)
  if (returnFocus) (trigger.value?.querySelector('button') ?? trigger.value)?.focus()
}

function outside(event: Event) {
  if (!root.value?.contains(event.target as Node)) hide(false)
}

function onTriggerKey(event: KeyboardEvent) {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    show(event.key === 'ArrowDown' ? 'first' : 'last')
  }
}

function onMenuKey(event: KeyboardEvent) {
  const indices = enabled()
  const current = itemEls.indexOf(document.activeElement as HTMLButtonElement)
  const at = indices.indexOf(current)
  let target: number | undefined
  if (event.key === 'ArrowDown') target = indices[(at + 1) % indices.length]
  else if (event.key === 'ArrowUp') target = indices[(at - 1 + indices.length) % indices.length]
  else if (event.key === 'Home') target = indices[0]
  else if (event.key === 'End') target = indices[indices.length - 1]
  else if (event.key === 'Escape') { event.preventDefault(); hide(); return }
  else if (event.key === 'Tab') { hide(false); return }
  else if (event.key.length === 1 && /\S/.test(event.key)) {
    const letter = event.key.toLowerCase()
    const order = [...indices.slice(at + 1), ...indices.slice(0, at + 1)]
    target = order.find(i => props.items[i].label.toLowerCase().startsWith(letter))
  }
  if (target !== undefined) {
    event.preventDefault()
    itemEls[target]?.focus()
  }
}

function select(item: MenuAction) {
  if (item.disabled) return
  hide()
  item.onSelect()
}

onBeforeUnmount(() => document.removeEventListener('pointerdown', outside, true))
</script>

<template>
  <div ref="root" class="fabric-menu">
    <span ref="trigger" @keydown="onTriggerKey">
      <slot name="trigger" :open="open" :toggle="() => (open ? hide() : show())">
        <FabricButton variant="ghost" :icon="icon" :label="label" :aria-haspopup="'menu'" :aria-expanded="open" :aria-controls="`fabric-${id}`"
          @click="open ? hide() : show()" />
      </slot>
    </span>
    <Transition name="pop">
      <div v-if="open" :id="`fabric-${id}`" class="popover" :class="align" role="menu" :aria-label="label" @keydown="onMenuKey">
        <button v-for="(item, i) in items" :key="i" :ref="(el: unknown) => (itemEls[i] = el as HTMLButtonElement)" type="button" role="menuitem" class="item"
          :class="item.tone" :aria-disabled="item.disabled || undefined" tabindex="-1" @click="select(item)">
          <FabricIcon v-if="item.icon" :name="item.icon" :size="16" />
          {{ item.label }}
          <span class="fabric-sr-only">{{ i + 1 }} of {{ items.length }}</span>
        </button>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.fabric-menu { position: relative; display: inline-flex; }
.popover {
  position: absolute; top: calc(100% + 4px); z-index: 60; min-width: 180px; padding: 4px; display: flex; flex-direction: column;
  background: var(--fabric-surface-raised); border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-md); box-shadow: var(--fabric-shadow-md);
}
.end { right: 0; }
.start { left: 0; }
.item {
  display: flex; align-items: center; gap: 8px; padding: 7px 10px; border: 0; border-radius: var(--fabric-radius-sm); background: none;
  color: var(--fabric-ink); font: var(--fabric-text-sm) var(--fabric-font); text-align: left; white-space: nowrap; cursor: pointer;
}
.item:hover, .item:focus { outline: none; background: var(--fabric-accent-soft); color: var(--fabric-accent); }
.item.danger { color: var(--fabric-bad); }
.item.danger:hover, .item.danger:focus { background: var(--fabric-bad-soft); color: var(--fabric-bad); }
.item[aria-disabled="true"] { opacity: .45; cursor: default; }
.item[aria-disabled="true"]:hover { background: none; color: var(--fabric-ink); }
.pop-enter-active, .pop-leave-active { transition: opacity var(--fabric-fast), transform var(--fabric-fast) var(--fabric-ease); }
.pop-enter-from, .pop-leave-to { opacity: 0; transform: translateY(-4px); }
</style>
