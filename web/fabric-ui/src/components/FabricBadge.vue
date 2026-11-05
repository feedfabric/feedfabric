<script setup lang="ts">
// A short status or count. dot shows a coloured dot before the text, for statuses read at a glance.
withDefaults(defineProps<{ tone?: 'neutral' | 'accent' | 'good' | 'warn' | 'bad' | 'info'; dot?: boolean; size?: 'sm' | 'md' }>(),
  { tone: 'neutral', size: 'md' })
</script>

<template>
  <span class="fabric-badge" :class="[tone, size]"><span v-if="dot" class="dot" aria-hidden="true" /><slot /></span>
</template>

<style scoped>
.fabric-badge {
  display: inline-flex; align-items: center; gap: 5px; padding: 2px 8px; border-radius: var(--fabric-radius-full);
  font: 600 var(--fabric-text-xs)/1.4 var(--fabric-font); white-space: nowrap; font-variant-numeric: tabular-nums;
  background: var(--soft); color: var(--strong);
}
.sm { padding: 0 6px; font-size: 11px; }
.dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
.neutral { --soft: var(--fabric-line); --strong: var(--fabric-muted); }
.accent { --soft: var(--fabric-accent-soft); --strong: var(--fabric-accent); }
.good { --soft: var(--fabric-good-soft); --strong: var(--fabric-good); }
.warn { --soft: var(--fabric-warn-soft); --strong: var(--fabric-warn); }
.bad { --soft: var(--fabric-bad-soft); --strong: var(--fabric-bad); }
.info { --soft: var(--fabric-info-soft); --strong: var(--fabric-info); }
</style>
