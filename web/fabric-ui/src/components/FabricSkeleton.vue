<script setup lang="ts">
// A placeholder shaped like what's loading: a line, several lines (the last one shorter), or a circle
withDefaults(defineProps<{ width?: string; height?: string; lines?: number; circle?: boolean }>(), { width: '100%', height: '12px', lines: 1 })
</script>

<template>
  <span v-if="circle" class="fabric-skeleton circle" :style="{ width, height: width }" aria-hidden="true" />
  <span v-else class="fabric-skeleton-lines" aria-hidden="true">
    <span v-for="n in lines" :key="n" class="fabric-skeleton" :style="{ width: n === lines && lines > 1 ? '60%' : width, height }" />
  </span>
</template>

<style scoped>
.fabric-skeleton-lines { display: flex; flex-direction: column; gap: 8px; width: 100%; }
.fabric-skeleton { display: block; border-radius: 6px; background: var(--fabric-line); animation: fabric-shimmer 1.4s ease-in-out infinite; }
.circle { border-radius: 50%; flex: none; }
@media (prefers-reduced-motion: reduce) { .fabric-skeleton { animation: none; } }
</style>
