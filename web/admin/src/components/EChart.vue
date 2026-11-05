<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import type { ECharts, EChartsOption } from 'echarts'
import { useTheme } from '@feedfabric/fabric-ui'

// An ECharts chart that follows its option and its box's size. ECharts loads on first use, so pages without
// charts never download it. It redraws in the other colours when the theme changes (FabricUI's useTheme). Name it
// with `label` and say what it shows in `summary`, for screen readers.
const props = defineProps<{ option: EChartsOption; height?: number; label: string; summary?: string }>()

const el = ref<HTMLElement>()
let chart: ECharts | undefined
let resize: ResizeObserver | undefined
const { isDark } = useTheme()

// ECharts sets its theme when a chart is created, so a theme change means creating it again
async function draw() {
  const echarts = await import('echarts')
  if (!el.value) return
  chart?.dispose()
  chart = echarts.init(el.value, isDark.value ? 'dark' : undefined, { renderer: 'svg' })
  chart.setOption({ backgroundColor: 'transparent', ...props.option })
}

onMounted(async () => {
  await draw()
  if (!el.value) return
  resize = new ResizeObserver(() => chart?.resize())
  resize.observe(el.value)
})

watch(isDark, draw)

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
