<script setup lang="ts">
// One screen: a heading with an optional description and actions, then its content
defineProps<{ title: string; description?: string }>()
</script>

<template>
  <div class="fabric-page">
    <header class="head">
      <div class="titles">
        <slot name="eyebrow" />
        <h1>{{ title }}</h1>
        <p v-if="description" class="description">{{ description }}</p>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <slot />
  </div>
</template>

<style scoped>
.fabric-page { display: flex; flex-direction: column; gap: var(--fabric-space-4); min-width: 0; }
.head { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--fabric-space-2) var(--fabric-space-4); flex-wrap: wrap; }
.titles { min-width: 0; }
h1 { margin: 0; font-size: var(--fabric-text-xl); letter-spacing: -.01em; line-height: 1.25; }
.description { margin: 4px 0 0; color: var(--fabric-muted); font-size: var(--fabric-text-sm); max-width: 70ch; }
.actions { display: flex; flex-wrap: wrap; gap: var(--fabric-space-2); align-items: center; }
</style>
