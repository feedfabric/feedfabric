<script setup lang="ts">
// A surface for one piece of content. title and subtitle make a header, with an actions slot beside it;
// padding="none" lets lists and tables run to the edges.
withDefaults(defineProps<{ title?: string; subtitle?: string; padding?: 'none' | 'sm' | 'md' | 'lg'; as?: string; interactive?: boolean }>(),
  { padding: 'md', as: 'section' })
</script>

<template>
  <component :is="as" class="fabric-card" :class="[`pad-${padding}`, { interactive }]">
    <header v-if="title || $slots.actions || $slots.header" class="head">
      <slot name="header">
        <div class="titles">
          <h2 v-if="title" class="title">{{ title }}</h2>
          <p v-if="subtitle" class="subtitle">{{ subtitle }}</p>
        </div>
      </slot>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <div class="body"><slot /></div>
    <footer v-if="$slots.footer" class="foot"><slot name="footer" /></footer>
  </component>
</template>

<style scoped>
.fabric-card {
  --pad: var(--fabric-space-4);
  background: var(--fabric-surface); border: 1px solid var(--fabric-line); border-radius: var(--fabric-radius-lg);
  box-shadow: var(--fabric-shadow-sm); min-width: 0; display: flex; flex-direction: column;
}
.pad-none { --pad: 0px; overflow: hidden; }
.pad-sm { --pad: var(--fabric-space-3); }
.pad-lg { --pad: var(--fabric-space-6); }
.body { padding: var(--pad); min-width: 0; flex: 1; }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--fabric-space-3);
  padding: var(--fabric-space-4) var(--fabric-space-4) 0; }
.pad-none .head { padding-bottom: var(--fabric-space-3); border-bottom: 1px solid var(--fabric-line); }
.titles { min-width: 0; }
.title { margin: 0; font-size: var(--fabric-text-xs); font-weight: 600; text-transform: uppercase; letter-spacing: .06em; color: var(--fabric-muted); }
.subtitle { margin: 2px 0 0; font-size: var(--fabric-text-sm); color: var(--fabric-muted); }
.actions { display: flex; gap: var(--fabric-space-2); flex: none; }
.head + .body { padding-top: var(--fabric-space-3); }
.pad-none .head + .body { padding-top: 0; }
.foot { padding: var(--fabric-space-3) var(--fabric-space-4); border-top: 1px solid var(--fabric-line); }
.interactive { transition: border-color var(--fabric-fast), box-shadow var(--fabric-fast); }
.interactive:hover { border-color: var(--fabric-line-strong); box-shadow: var(--fabric-shadow-md); }
</style>
