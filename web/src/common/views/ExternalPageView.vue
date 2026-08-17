<script setup lang="ts">
/**
 * 外链承载页：把菜单里配置的外部地址用 iframe 内嵌在主界面标签内显示，
 * 使主窗口里点开的菜单一律在内部展示（内嵌方式与 HangfireView 一致）。
 * 目标地址与标签标题由 query 传入：/external?url=...&title=...
 *
 * 注意：部分站点用 X-Frame-Options / CSP frame-ancestors 禁止被内嵌，
 * 此时 iframe 会显示空白或拒绝提示，只能改用独立窗口，故保留顶部按钮。
 */
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { openExternalWindow } from '@/common/menuLink'

const route = useRoute()

const url = computed(() => String(route.query.url || ''))
const title = computed(() => String(route.query.title || ''))

// 改 key 强制 iframe 重新挂载；直接把 src 赋成同一地址不会触发重新加载
const reloadKey = ref(0)

function reload() {
  reloadKey.value += 1
}

function openWindow() {
  openExternalWindow({ page: url.value, title: title.value })
}
</script>

<template>
  <div class="external-page">
    <div class="external-bar">
      <span class="external-url" :title="url">{{ url }}</span>
      <span class="external-hint">页面空白说明该站点禁止被内嵌，请改用独立窗口</span>
      <el-button size="small" @click="reload">刷新</el-button>
      <el-button size="small" @click="openWindow">独立窗口打开</el-button>
    </div>
    <iframe v-if="url" :key="reloadKey" :src="url" frameborder="0" class="external-frame" />
    <div v-else class="external-empty">未指定要打开的地址</div>
  </div>
</template>

<style scoped>
.external-page {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
}
.external-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  background: var(--el-fill-color-blank);
}
.external-url {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.external-hint {
  flex: none;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}
.external-frame {
  flex: 1;
  width: 100%;
  border: none;
}
.external-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--el-text-color-placeholder);
}
</style>
