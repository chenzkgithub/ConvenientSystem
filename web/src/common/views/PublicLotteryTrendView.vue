<script setup lang="ts">
/**
 * 走势图公开访问页：供外部链接直接访问，无需登录。
 * URL 格式：/lottery-trend?type=DLT&public=1
 * - 自动加载彩种配置以获取选号分区（走势图渲染依赖）
 * - 不显示选号保存功能（公开用户无权限保存）
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getLotteryConfig, type LotteryConfig, type LotteryZone } from '@/common/lottery'
import LotteryTrendView from './LotteryTrendView.vue'

const route = useRoute()
const type = computed(() => String(route.query.type || 'DLT'))

const config = ref<LotteryConfig | null>(null)
const pickZones = computed<LotteryZone[]>(() => config.value?.pickZones ?? [])

async function loadConfig() {
  try {
    config.value = await getLotteryConfig(type.value)
  } catch {
    config.value = null
  }
}

onMounted(() => loadConfig())
watch(type, () => loadConfig())

/** 彩种名称 → 设置 document.title */
const typeName = computed(() => config.value?.name ?? type.value)
watch(typeName, (name) => { document.title = `${name} 走势图` }, { immediate: true })
</script>

<template>
  <div class="public-trend-page">
    <div class="public-trend-content">
      <LotteryTrendView v-if="config" :type="type" :pick-zones="pickZones" public-mode />
    </div>
  </div>
</template>

<style scoped>
.public-trend-page {
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: #f5f7fa;
  overflow: hidden;
}

.public-trend-content {
  flex: 1;
  overflow: hidden;
  padding: 12px;
}

.public-trend-content > :deep(.trend-container) {
  height: 100%;
}
</style>
