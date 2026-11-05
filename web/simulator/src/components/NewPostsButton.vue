<script setup lang="ts">
// The pill above a feed that brings in posts that arrived since it was drawn
defineProps<{ count: number }>()
defineEmits<{ show: [] }>()
</script>

<template>
  <button v-if="count" class="pill-button" @click="$emit('show')">↑ Show {{ count }} new {{ count === 1 ? 'post' : 'posts' }}</button>
</template>
