<script setup lang="ts">
import { computed } from 'vue'
import { fmt } from '../format'

// "51–100 of 1,006" with previous and next
const props = defineProps<{ page: number; size: number; total: number; shown: number }>()
const emit = defineEmits<{ go: [page: number] }>()
const first = computed(() => (props.total ? (props.page - 1) * props.size + 1 : 0))
const last = computed(() => (props.page - 1) * props.size + props.shown)
const pages = computed(() => Math.max(1, Math.ceil(props.total / props.size)))
</script>

<template>
  <div class="pager">
    <span class="muted">{{ fmt(first) }}–{{ fmt(last) }} of {{ fmt(total) }}</span>
    <span class="buttons">
      <button :disabled="page <= 1" @click="emit('go', page - 1)">Previous</button>
      <button :disabled="page >= pages" @click="emit('go', page + 1)">Next</button>
    </span>
  </div>
</template>

<style scoped>
.buttons { display: flex; gap: 6px; }
</style>
