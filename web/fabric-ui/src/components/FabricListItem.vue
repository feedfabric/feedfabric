<script setup lang="ts">
// One row: something to recognise it by on the left (leading slot), a title with a subtitle, and a meta value or
// actions on the right. With href or clickable the whole row is one target; actions inside stay separately pressable.
defineProps<{ title?: string; subtitle?: string; meta?: string; href?: string; clickable?: boolean; selected?: boolean }>()
const emit = defineEmits<{ select: [] }>()
</script>

<template>
  <li class="fabric-list-item" :class="{ interactive: href || clickable, selected }">
    <span v-if="$slots.leading" class="leading"><slot name="leading" /></span>
    <span class="main">
      <a v-if="href" :href="href" class="title target">{{ title }}<slot name="title" /></a>
      <button v-else-if="clickable" type="button" class="title target" :aria-pressed="selected" @click="emit('select')">
        {{ title }}<slot name="title" />
      </button>
      <span v-else class="title">{{ title }}<slot name="title" /></span>
      <span v-if="subtitle || $slots.subtitle" class="subtitle">{{ subtitle }}<slot name="subtitle" /></span>
      <slot />
    </span>
    <span v-if="meta || $slots.trailing" class="trailing">
      <span v-if="meta" class="meta">{{ meta }}</span>
      <slot name="trailing" />
    </span>
  </li>
</template>

<style scoped>
.fabric-list-item { position: relative; display: flex; align-items: center; gap: var(--fabric-space-3); padding: 10px var(--fabric-space-4); min-width: 0; }
.leading { flex: none; display: flex; }
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.title { font-size: var(--fabric-text-md); font-weight: 500; color: var(--fabric-ink); overflow-wrap: anywhere; text-align: left; }
button.title { border: 0; padding: 0; background: none; font: inherit; font-weight: 500; cursor: pointer; }
.subtitle { font-size: var(--fabric-text-sm); color: var(--fabric-muted); overflow-wrap: anywhere; }
.trailing { flex: none; display: flex; align-items: center; gap: var(--fabric-space-2); position: relative; z-index: 1; }
.meta { font-size: var(--fabric-text-sm); color: var(--fabric-muted); font-variant-numeric: tabular-nums; white-space: nowrap; }
a.title { text-decoration: none; }
/* The title's link or button covers the whole row, so the row is one big target */
.target::after { content: ''; position: absolute; inset: 0; }
.target:focus-visible { outline: none; }
.target:focus-visible::after { box-shadow: inset var(--fabric-focus); border-radius: 4px; }
.interactive { transition: background var(--fabric-fast); }
.interactive:hover { background: color-mix(in srgb, var(--fabric-accent) 5%, transparent); }
.interactive:hover .title { color: var(--fabric-accent); }
.selected { background: var(--fabric-accent-soft); }
</style>
