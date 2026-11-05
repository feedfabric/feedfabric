<script setup lang="ts">
import { useTheme, type ThemeChoice } from '../composables/useTheme'
import type { SegmentOption } from '../types'
import FabricSegmented from './FabricSegmented.vue'

// Light, system or dark, remembered in this browser
const { theme } = useTheme()
const options: SegmentOption<ThemeChoice>[] = [
  { value: 'light', label: 'Light', icon: 'sun', iconOnly: true },
  { value: 'system', label: 'Match system', icon: 'monitor', iconOnly: true },
  { value: 'dark', label: 'Dark', icon: 'moon', iconOnly: true },
]
</script>

<template>
  <FabricSegmented v-model="theme" :options="options" label="Colour theme" size="sm" />
</template>
