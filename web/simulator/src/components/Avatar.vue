<script setup lang="ts">
// A person's initial in a tinted circle; decorative, as their name is always written beside it
defineProps<{ name: string; size?: number }>()
</script>

<template>
  <span class="avatar-initial" :style="{ '--size': `${size ?? 32}px` }" aria-hidden="true">{{ name[0]?.toUpperCase() }}</span>
</template>

<style scoped>
.avatar-initial { width: var(--size); height: var(--size); flex: none; border-radius: 50%; display: grid; place-items: center;
  background: var(--accent-soft); color: var(--accent); font-weight: 700; font-size: calc(var(--size) * .42); }
</style>
