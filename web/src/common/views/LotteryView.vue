<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getLotteryConfig, type LotteryConfig, type LotteryZone } from '@/common/lottery'
import LotteryTrendView from './LotteryTrendView.vue'
import LotteryPickPanel from '@/common/components/LotteryPickPanel.vue'

/**
 * 彩票菜单页：走势图与选号两个独立页签。
 * - 走势图：LotteryTrendView（独立组件）
 * - 选号界面：LotteryPickPanel（独立组件，选号记录页弹窗亦复用）
 */

// 彩种代码来自菜单路由 query（/lottery?type=SSQ），默认大乐透
const route = useRoute()
const type = computed(() => String(route.query.type || 'DLT'))

const activeTab = ref('trend')

// 走势图需要的选号分区配置（选号面板内部自行加载一份）
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

// 菜单切彩种时路由 path 不变仅 query 变化，组件实例被复用 onMounted 不重执行：重新加载配置
watch(type, () => loadConfig())
</script>

<template>
  <div class="lottery-page">
    <!-- 页签 -->
    <el-tabs v-model="activeTab" class="lottery-tabs">
      <el-tab-pane label="走势图" name="trend">
        <LotteryTrendView :type="type" :pick-zones="pickZones" />
      </el-tab-pane>
      <el-tab-pane label="选号" name="pick">
        <LotteryPickPanel :type="type" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.lottery-page {
  padding: 12px 16px;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.lottery-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.lottery-tabs :deep(.el-tabs__header) {
  margin-bottom: 12px;
  flex-shrink: 0;
}

.lottery-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow: hidden;
}

.lottery-tabs :deep(.el-tab-pane) {
  height: 100%;
  display: flex;
  flex-direction: column;
}
</style>
