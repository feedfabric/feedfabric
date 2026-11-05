<script setup lang="ts">
// Responsive columns: as many as fit at least min wide (so it reflows on phones by itself), or exactly cols
withDefaults(defineProps<{ min?: string; cols?: number; gap?: 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 }>(), { min: '240px', gap: 4 })
</script>

<template>
  <div class="fabric-grid" :style="{
    gap: `var(--fabric-space-${gap})`,
    gridTemplateColumns: cols ? `repeat(${cols}, minmax(0, 1fr))` : `repeat(auto-fit, minmax(min(100%, ${min}), 1fr))`,
  }">
    <slot />
  </div>
</template>

<style scoped>
.fabric-grid { display: grid; min-width: 0; }
</style>
