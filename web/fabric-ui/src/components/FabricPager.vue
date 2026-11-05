<script setup lang="ts">
import FabricButton from './FabricButton.vue'

// Previous and next for cursor-paged lists, where the total isn't known. summary says where you are in words and is
// announced as it changes; feeds can call the buttons Newer and Older.
withDefaults(defineProps<{ hasPrevious?: boolean; hasNext?: boolean; summary?: string; loading?: boolean; previousLabel?: string; nextLabel?: string }>(),
  { previousLabel: 'Previous', nextLabel: 'Next' })
const emit = defineEmits<{ previous: []; next: [] }>()
</script>

<template>
  <nav class="fabric-pager" aria-label="Pages">
    <FabricButton size="sm" icon="chevron-left" :disabled="!hasPrevious || loading" @click="emit('previous')">{{ previousLabel }}</FabricButton>
    <span v-if="summary" class="summary" aria-live="polite">{{ summary }}</span>
    <FabricButton size="sm" icon-after="chevron-right" :disabled="!hasNext || loading" @click="emit('next')">{{ nextLabel }}</FabricButton>
  </nav>
</template>

<style scoped>
.fabric-pager { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 10px var(--fabric-space-4); border-top: 1px solid var(--fabric-line); }
.summary { font-size: var(--fabric-text-sm); color: var(--fabric-muted); text-align: center; }
</style>
