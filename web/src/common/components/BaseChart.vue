<script setup lang="ts">
/**
 * 通用 ECharts 图表组件（按需引入核心 + 常用图表/组件，控制打包体积）。
 * - 用法：<BaseChart :option="echartsOption" height="300px" />
 * - option 变化自动重绘，容器尺寸变化自动 resize。
 */
import { ref, onMounted, onBeforeUnmount, watch, shallowRef } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart, LineChart, PieChart } from 'echarts/charts'
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
  TitleComponent,
  DatasetComponent,
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsCoreOption } from 'echarts/core'

echarts.use([
  BarChart, LineChart, PieChart,
  GridComponent, TooltipComponent, LegendComponent, TitleComponent, DatasetComponent,
  CanvasRenderer,
])

const props = withDefaults(defineProps<{
  option: EChartsCoreOption
  height?: string
}>(), {
  height: '300px',
})

const el = ref<HTMLDivElement | null>(null)
const chart = shallowRef<echarts.ECharts | null>(null)
let ro: ResizeObserver | null = null

function render() {
  if (!chart.value) return
  // notMerge=true：清掉上一次残留系列，避免筛选切换后旧数据叠加
  chart.value.setOption(props.option, true)
}

onMounted(() => {
  if (!el.value) return
  chart.value = echarts.init(el.value)
  render()
  // rAF 节流：resize 会再次改变尺寸，同步回调容易触发 ResizeObserver loop 良性警告
  ro = new ResizeObserver(() => requestAnimationFrame(() => chart.value?.resize()))
  ro.observe(el.value)
})

watch(() => props.option, render, { deep: true })

onBeforeUnmount(() => {
  ro?.disconnect()
  ro = null
  chart.value?.dispose()
  chart.value = null
})
</script>

<template>
  <div ref="el" class="base-chart" :style="{ height }"></div>
</template>

<style scoped>
.base-chart {
  width: 100%;
}
</style>
