<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import type { ECharts, EChartsOption } from 'echarts'

// An ECharts chart that follows its option and its box's size. ECharts loads on first use, so pages without
// charts never download it. Name it with `label` and say what it shows in `summary`, for screen readers.
const props = defineProps<{ option: EChartsOption; height?: number; label: string; summary?: string }>()

const el = ref<HTMLElement>()
let chart: ECharts | undefined
let resize: ResizeObserver | undefined

onMounted(async () => {
  const echarts = await import('echarts')
  if (!el.value) return
  const dark = window.matchMedia('(prefers-color-scheme: dark)').matches
  chart = echarts.init(el.value, dark ? 'dark' : undefined, { renderer: 'svg' })
  chart.setOption({ backgroundColor: 'transparent', ...props.option })
  resize = new ResizeObserver(() => chart?.resize())
  resize.observe(el.value)
})

watch(() => props.option, option => chart?.setOption({ backgroundColor: 'transparent', ...option }, { notMerge: true }), { deep: true })

onUnmounted(() => {
  resize?.disconnect()
  chart?.dispose()
})
</script>

<template>
  <figure class="chart">
    <div ref="el" role="img" :aria-label="summary ? `${label}. ${summary}` : label" :style="{ height: `${height ?? 240}px` }" />
  </figure>
</template>

<style scoped>
.chart { margin: 0; }
</style>
