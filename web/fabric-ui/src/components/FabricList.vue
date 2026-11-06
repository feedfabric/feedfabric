<script setup lang="ts">
// Rows of FabricListItem with a hairline between them. Put it in a FabricCard with padding="none" to run edge to edge.
withDefaults(defineProps<{ label?: string; ordered?: boolean }>(), {})
</script>

<template>
  <component :is="ordered ? 'ol' : 'ul'" class="fabric-list" :aria-label="label">
    <slot />
  </component>
</template>

<style scoped>
.fabric-list { list-style: none; margin: 0; padding: 0; }
.fabric-list > :deep(li + li) { border-top: 1px solid var(--fabric-line); }
</style>
