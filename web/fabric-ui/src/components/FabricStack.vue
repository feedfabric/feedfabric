<script setup lang="ts">
// Lays its children out in a row or column with a gap from the spacing scale (0 to 8)
withDefaults(defineProps<{
  direction?: 'row' | 'column'
  gap?: 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8
  align?: 'start' | 'center' | 'end' | 'stretch' | 'baseline'
  justify?: 'start' | 'center' | 'end' | 'between'
  wrap?: boolean
  as?: string
}>(), { direction: 'column', gap: 4, align: 'stretch', justify: 'start', as: 'div' })
</script>

<template>
  <component :is="as" class="fabric-stack" :style="{
    flexDirection: direction, gap: `var(--fabric-space-${gap})`, flexWrap: wrap ? 'wrap' : 'nowrap',
    alignItems: align === 'start' || align === 'end' ? `flex-${align}` : align,
    justifyContent: justify === 'between' ? 'space-between' : justify === 'start' || justify === 'end' ? `flex-${justify}` : justify,
  }">
    <slot />
  </component>
</template>

<style scoped>
.fabric-stack { display: flex; min-width: 0; }
</style>
