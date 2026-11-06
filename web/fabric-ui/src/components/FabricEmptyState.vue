<script setup lang="ts">
import FabricIcon, { type IconName } from './FabricIcon.vue'

// What a list or page shows when there's nothing in it yet: what would be here, and what to do about it
withDefaults(defineProps<{ title: string; description?: string; icon?: IconName; compact?: boolean }>(), { icon: 'sparkle' })
</script>

<template>
  <div class="fabric-empty" :class="{ compact }">
    <span class="icon"><FabricIcon :name="icon" :size="compact ? 18 : 22" /></span>
    <p class="title">{{ title }}</p>
    <p v-if="description" class="description">{{ description }}</p>
    <div v-if="$slots.default" class="actions"><slot /></div>
  </div>
</template>

<style scoped>
.fabric-empty { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 6px; padding: var(--fabric-space-7) var(--fabric-space-4); }
.compact { padding: var(--fabric-space-5) var(--fabric-space-3); }
.icon { display: grid; place-items: center; width: 44px; height: 44px; margin-bottom: 4px; border-radius: 50%; background: var(--fabric-accent-soft); color: var(--fabric-accent); }
.compact .icon { width: 34px; height: 34px; }
.title { margin: 0; font-weight: 600; }
.description { margin: 0; max-width: 46ch; font-size: var(--fabric-text-sm); color: var(--fabric-muted); }
.actions { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--fabric-space-2); margin-top: var(--fabric-space-2); }
</style>
