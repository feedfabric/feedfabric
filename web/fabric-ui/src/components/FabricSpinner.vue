<script setup lang="ts">
// A small spinning ring for work in progress; give it a label when nothing else says what's loading
withDefaults(defineProps<{ size?: number; label?: string }>(), { size: 16 })
</script>

<template>
  <span class="fabric-spinner" :style="{ width: `${size}px`, height: `${size}px` }" :role="label ? 'status' : undefined"
    :aria-label="label" :aria-hidden="label ? undefined : 'true'" />
</template>

<style scoped>
.fabric-spinner {
  display: inline-block; flex: none; border-radius: 50%;
  border: 2px solid color-mix(in srgb, currentColor 25%, transparent); border-top-color: currentColor;
  animation: fabric-spin .7s linear infinite;
}
@media (prefers-reduced-motion: reduce) { .fabric-spinner { animation-duration: 2s; } }
</style>
