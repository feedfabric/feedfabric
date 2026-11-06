<script setup lang="ts">
import { computed, ref, watch } from 'vue'

// A person's picture, or their initials on a colour picked from their name so the same person always looks the
// same. Falls back to initials if the picture fails to load.
const props = withDefaults(defineProps<{ name: string; src?: string | null; size?: number; decorative?: boolean }>(), { size: 32 })

const failed = ref(false)
watch(() => props.src, () => (failed.value = false))

const initials = computed(() => {
  const words = props.name.replace(/[@_.-]+/g, ' ').trim().split(/\s+/).filter(Boolean)
  const letters = words.length > 1 ? words[0][0] + words[words.length - 1][0] : (words[0] ?? '?').slice(0, 2)
  return letters.toUpperCase()
})

// One of eight hues, the same for the same name everywhere
const hue = computed(() => {
  let hash = 0
  for (const char of props.name) hash = (hash * 31 + char.charCodeAt(0)) | 0
  return [250, 200, 160, 120, 30, 340, 290, 180][Math.abs(hash) % 8]
})
</script>

<template>
  <span class="fabric-avatar" :style="{ width: `${size}px`, height: `${size}px`, fontSize: `${Math.round(size * 0.4)}px`, '--hue': hue }"
    :role="decorative ? undefined : 'img'" :aria-label="decorative ? undefined : name" :aria-hidden="decorative ? 'true' : undefined">
    <img v-if="src && !failed" :src="src" alt="" @error="failed = true">
    <span v-else aria-hidden="true">{{ initials }}</span>
  </span>
</template>

<style scoped>
.fabric-avatar {
  display: inline-grid; place-items: center; flex: none; border-radius: 50%; overflow: hidden; font-weight: 600; user-select: none;
  background: hsl(var(--hue) 55% 88%); color: hsl(var(--hue) 45% 30%);
}
:root[data-theme="dark"] .fabric-avatar { background: hsl(var(--hue) 30% 26%); color: hsl(var(--hue) 60% 82%); }
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) .fabric-avatar { background: hsl(var(--hue) 30% 26%); color: hsl(var(--hue) 60% 82%); }
}
img { width: 100%; height: 100%; object-fit: cover; }
</style>
