<script setup lang="ts">
import { computed } from 'vue'
import FabricIcon from './FabricIcon.vue'

// A message about the page or something on it. Problems (bad) are announced straight away; everything else
// politely. dismissible adds a close button that emits dismiss; the parent decides whether it goes.
const props = withDefaults(defineProps<{ tone?: 'info' | 'good' | 'warn' | 'bad'; title?: string; dismissible?: boolean }>(), { tone: 'info' })
const emit = defineEmits<{ dismiss: [] }>()

const icon = computed(() => ({ info: 'info', good: 'check-circle', warn: 'alert', bad: 'alert' } as const)[props.tone])
</script>

<template>
  <div class="fabric-notice" :class="tone" :role="tone === 'bad' ? 'alert' : 'status'">
    <FabricIcon :name="icon" :size="18" class="icon" />
    <div class="body">
      <p v-if="title" class="title">{{ title }}</p>
      <div class="text"><slot /></div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </div>
    <button v-if="dismissible" type="button" class="close" aria-label="Dismiss" @click="emit('dismiss')"><FabricIcon name="x" :size="16" /></button>
  </div>
</template>

<style scoped>
.fabric-notice {
  display: flex; gap: 10px; padding: 10px 12px; border-radius: var(--fabric-radius-md);
  background: var(--soft); border: 1px solid color-mix(in srgb, var(--strong) 25%, transparent); color: var(--fabric-ink);
}
.info { --soft: var(--fabric-info-soft); --strong: var(--fabric-info); }
.good { --soft: var(--fabric-good-soft); --strong: var(--fabric-good); }
.warn { --soft: var(--fabric-warn-soft); --strong: var(--fabric-warn); }
.bad { --soft: var(--fabric-bad-soft); --strong: var(--fabric-bad); }
.icon { color: var(--strong); margin-top: 2px; }
.body { flex: 1; min-width: 0; font-size: var(--fabric-text-sm); }
.title { margin: 0 0 2px; font-weight: 600; }
.text :deep(p) { margin: 0; }
.actions { display: flex; flex-wrap: wrap; gap: var(--fabric-space-2); margin-top: var(--fabric-space-2); }
.close { flex: none; display: grid; place-items: center; width: 26px; height: 26px; margin: -3px -4px 0 0; border: 0; border-radius: 50%;
  background: none; color: var(--fabric-muted); cursor: pointer; }
.close:hover { background: color-mix(in srgb, var(--strong) 15%, transparent); color: var(--fabric-ink); }
.close:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
</style>
