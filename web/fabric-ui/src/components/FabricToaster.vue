<script setup lang="ts">
import { onBeforeUnmount, watch } from 'vue'
import { dismissToast, toasts, type Toast } from '../composables/useToast'
import FabricIcon from './FabricIcon.vue'

// Shows the toast queue (useToast) in the corner. Put one near the root of the app. Problems are announced at once,
// other toasts politely. A toast's timer pauses while the pointer or keyboard focus is on it.
const timers = new Map<number, { remaining: number; started: number; handle?: ReturnType<typeof setTimeout> }>()

function start(t: Toast) {
  const timer = timers.get(t.id)
  if (!timer || timer.handle) return
  timer.started = Date.now()
  timer.handle = setTimeout(() => dismissToast(t.id), timer.remaining)
}

function pause(t: Toast) {
  const timer = timers.get(t.id)
  if (!timer?.handle) return
  clearTimeout(timer.handle)
  timer.handle = undefined
  timer.remaining -= Date.now() - timer.started
}

watch(() => toasts.map(t => t.id), ids => {
  for (const t of toasts) {
    if (t.timeout > 0 && !timers.has(t.id)) {
      timers.set(t.id, { remaining: t.timeout, started: 0 })
      start(t)
    }
  }
  for (const [id, timer] of timers) {
    if (!ids.includes(id)) {
      clearTimeout(timer.handle)
      timers.delete(id)
    }
  }
}, { immediate: true })

onBeforeUnmount(() => timers.forEach(t => clearTimeout(t.handle)))

const icons = { info: 'info', good: 'check-circle', warn: 'alert', bad: 'alert' } as const

function act(t: Toast) {
  t.action?.onSelect()
  dismissToast(t.id)
}
</script>

<template>
  <div class="fabric-toaster">
    <div class="region" aria-live="polite" aria-relevant="additions">
      <TransitionGroup name="toast">
        <div v-for="t in toasts.filter(x => x.tone !== 'bad')" :key="t.id" class="toast" :class="t.tone" role="status"
          @mouseenter="pause(t)" @mouseleave="start(t)" @focusin="pause(t)" @focusout="start(t)">
          <FabricIcon :name="icons[t.tone]" :size="18" class="icon" />
          <div class="body"><p class="title">{{ t.title }}</p><p v-if="t.message" class="message">{{ t.message }}</p></div>
          <button v-if="t.action" type="button" class="action" @click="act(t)">{{ t.action.label }}</button>
          <button type="button" class="close" aria-label="Dismiss" @click="dismissToast(t.id)"><FabricIcon name="x" :size="14" /></button>
        </div>
      </TransitionGroup>
    </div>
    <div class="region" aria-live="assertive">
      <TransitionGroup name="toast">
        <div v-for="t in toasts.filter(x => x.tone === 'bad')" :key="t.id" class="toast bad" role="alert">
          <FabricIcon :name="icons.bad" :size="18" class="icon" />
          <div class="body"><p class="title">{{ t.title }}</p><p v-if="t.message" class="message">{{ t.message }}</p></div>
          <button v-if="t.action" type="button" class="action" @click="act(t)">{{ t.action.label }}</button>
          <button type="button" class="close" aria-label="Dismiss" @click="dismissToast(t.id)"><FabricIcon name="x" :size="14" /></button>
        </div>
      </TransitionGroup>
    </div>
  </div>
</template>

<style scoped>
.fabric-toaster {
  position: fixed; z-index: 1000; right: 16px; bottom: 16px; display: flex; flex-direction: column; gap: 8px;
  width: min(380px, calc(100vw - 32px)); pointer-events: none;
}
.region { display: flex; flex-direction: column; gap: 8px; }
.toast {
  pointer-events: auto; display: flex; align-items: flex-start; gap: 10px; padding: 10px 10px 10px 12px;
  border-radius: var(--fabric-radius-md); background: var(--fabric-surface-raised); color: var(--fabric-ink);
  border: 1px solid var(--fabric-line); border-left: 3px solid var(--strong); box-shadow: var(--fabric-shadow-md);
}
.info { --strong: var(--fabric-info); }
.good { --strong: var(--fabric-good); }
.warn { --strong: var(--fabric-warn); }
.bad { --strong: var(--fabric-bad); }
.icon { color: var(--strong); margin-top: 1px; }
.body { flex: 1; min-width: 0; }
.title { margin: 0; font-size: var(--fabric-text-sm); font-weight: 600; }
.message { margin: 2px 0 0; font-size: var(--fabric-text-sm); color: var(--fabric-muted); overflow-wrap: anywhere; }
.action { border: 0; background: none; padding: 2px 6px; color: var(--fabric-accent); font: 600 var(--fabric-text-sm) var(--fabric-font); cursor: pointer; border-radius: 4px; }
.close { display: grid; place-items: center; width: 22px; height: 22px; border: 0; border-radius: 50%; background: none; color: var(--fabric-muted); cursor: pointer; }
.close:hover { background: var(--fabric-line); color: var(--fabric-ink); }
.action:focus-visible, .close:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
.toast-enter-active, .toast-leave-active { transition: opacity var(--fabric-slow) var(--fabric-ease), transform var(--fabric-slow) var(--fabric-ease); }
.toast-enter-from { opacity: 0; transform: translateY(8px); }
.toast-leave-to { opacity: 0; transform: translateX(16px); }
</style>
