<script setup lang="ts">
import { computed } from 'vue'
import { Loading, Tickets } from '@element-plus/icons-vue'
import { useAsyncTaskStore } from '@/common/stores/asyncTask'

// 顶栏异步任务中心：展示全部后台耗时任务（解决方案扫描/解析生成/Apifox 导入/批量删除等）实时进度。
// 数据由 asyncTask store 统一维护（submit 提交后轮询 + SignalR 推送双通道更新），本组件只读渲染；
// 完成/失败的 ElNotification 也由 store 弹出。运行中：图标旋转 + 蓝色角标 + 光圈。

const store = useAsyncTaskStore()

const list = computed(() => store.list)
const runningCount = computed(() => store.runningCount)
const hasFinished = computed(() => list.value.some(t => t.status !== 'running'))

const STATUS_META: Record<string, { type: 'primary' | 'success' | 'danger'; label: string }> = {
  running: { type: 'primary', label: '进行中' },
  succeeded: { type: 'success', label: '已完成' },
  failed: { type: 'danger', label: '失败' },
}

function statusOf(status: string) {
  return STATUS_META[status] ?? STATUS_META.running
}

function percentOf(t: { total: number; completed: number }) {
  if (!t.total) return 0
  return Math.min(100, Math.round((t.completed / t.total) * 100))
}

function formatTime(time?: string | null): string {
  if (!time) return ''
  // 后端下发 UTC 时间（DateTime 序列化可能无 Z 后缀）：统一按 UTC 解析后转本地展示，
  // 直接当本地时间渲染会慢 8 小时（如北京 15:53 显示成 07:53）
  const utc = /(Z|[+-]\d{2}:?\d{2})$/.test(time) ? time : `${time}Z`
  const d = new Date(utc)
  if (Number.isNaN(d.getTime())) return time.replace('T', ' ').slice(0, 16)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}
</script>

<template>
  <el-popover placement="bottom-end" :width="380" trigger="click" popper-class="async-task-popper">
    <template #reference>
      <el-badge :value="runningCount" :hidden="runningCount === 0" :max="99" type="primary" class="task-badge">
        <el-button circle size="small" title="异步任务" :class="{ 'task-running': runningCount > 0 }">
          <el-icon :size="14" :class="{ 'is-loading': runningCount > 0 }">
            <Loading v-if="runningCount > 0" />
            <Tickets v-else />
          </el-icon>
        </el-button>
      </el-badge>
    </template>

    <div class="task-popover">
      <div class="task-popover-header">
        <span class="task-popover-title">异步任务</span>
        <el-button v-if="hasFinished" link type="primary" size="small" @click="store.removeFinished()">清空已完成</el-button>
      </div>

      <div class="task-popover-body">
        <div v-if="list.length === 0" class="task-empty">暂无任务</div>
        <div v-for="t in list" :key="t.taskId" class="task-item">
          <div class="task-item-head">
            <el-tag :type="statusOf(t.status).type" size="small" class="task-status-tag" effect="light">
              {{ statusOf(t.status).label }}
            </el-tag>
            <span class="task-item-title" :title="t.title">{{ t.title }}</span>
            <span class="task-item-time">{{ formatTime(t.startedAt) }}</span>
          </div>
          <!-- 进度条：total 未知时不确定动画；完成/失败着色 -->
          <div class="task-progress">
            <el-progress
              :percentage="percentOf(t)"
              :indeterminate="t.total === 0 && t.status === 'running'"
              :duration="1.6"
              :stroke-width="6"
              :show-text="t.total > 0"
              :status="t.status === 'succeeded' ? 'success' : t.status === 'failed' ? 'exception' : undefined"
            />
            <span v-if="t.total > 0" class="task-count">
              {{ t.completed }}/{{ t.total }}<template v-if="t.failed > 0">（失败 {{ t.failed }}）</template>
            </span>
          </div>
          <div v-if="t.status === 'running' && t.current" class="task-current" :title="t.current">{{ t.current }}</div>
          <div v-if="t.status === 'failed' && t.error" class="task-error" :title="t.error">{{ t.error }}</div>
          <div v-if="t.status === 'succeeded'" class="task-summary">{{ store.summarize(t) }}</div>
        </div>
      </div>
    </div>
  </el-popover>
</template>

<style scoped>
.task-badge {
  line-height: 1;
}
/* 运行中：淡蓝光圈呼吸 */
.task-running {
  animation: task-pulse 1.6s ease-in-out infinite;
}
@keyframes task-pulse {
  0%, 100% { box-shadow: 0 0 0 3px rgba(64, 158, 255, 0.08); }
  50% { box-shadow: 0 0 0 5px rgba(64, 158, 255, 0.22); }
}
.task-popover {
  display: flex;
  flex-direction: column;
  max-height: 460px;
}
.task-popover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.task-popover-title {
  font-weight: 600;
  font-size: 14px;
}
.task-popover-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-top: 6px;
}
.task-empty {
  padding: 32px 0;
  text-align: center;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.task-item {
  padding: 8px;
  border-radius: 6px;
}
.task-item:hover {
  background: var(--el-fill-color-light);
}
.task-item-head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.task-status-tag {
  flex-shrink: 0;
}
.task-item-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  font-weight: 600;
}
.task-item-time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.task-progress {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 6px;
}
.task-progress :deep(.el-progress) {
  flex: 1;
  min-width: 0;
}
.task-count {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.task-current {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.task-error {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-color-danger);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.task-summary {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>