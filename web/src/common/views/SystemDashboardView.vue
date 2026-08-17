<script setup lang="ts">
/**
 * 系统运行状态大盘：展示服务器资源、Hangfire 任务统计、磁盘空间。
 */
import { onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { httpGet } from '@/api/request'

interface DashboardData {
  serverTime: string
  machineName: string
  osVersion: string
  dotNetVersion: string
  cpuCount: number
  processName: string
  workingSetMB: number
  privateMemoryMB: number
  threadCount: number
  handleCount: number
  startTime: string
  uptimeSeconds: number
  diskTotalGB: number
  diskFreeGB: number
  hangfireEnqueued: number
  hangfireScheduled: number
  hangfireProcessing: number
  hangfireSucceeded: number
  hangfireFailed: number
  hangfireRecurring: number
  hangfireServers: { name: string; workerCount: number; startedAt: string; heartbeat: string }[]
}

const loading = ref(false)
const data = ref<DashboardData | null>(null)

async function load() {
  loading.value = true
  try {
    data.value = await httpGet<DashboardData>('/api/Common/SystemDashboard/GetDashboard')
  } catch (e) {
    console.error('加载系统大盘失败', e)
  } finally {
    loading.value = false
  }
}

function formatUptime(seconds: number): string {
  const d = Math.floor(seconds / 86400)
  const h = Math.floor((seconds % 86400) / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (d > 0) return `${d}天 ${h}小时 ${m}分`
  if (h > 0) return `${h}小时 ${m}分`
  return `${m}分`
}

function diskPercent(): number {
  if (!data.value || data.value.diskTotalGB === 0) return 0
  return Math.round(((data.value.diskTotalGB - data.value.diskFreeGB) / data.value.diskTotalGB) * 100)
}

onMounted(load)
</script>

<template>
  <div class="dashboard-page" v-loading="loading">
    <div class="page-header">
      <h2>系统运行状态</h2>
      <el-button :icon="Refresh" size="small" @click="load" :loading="loading">刷新</el-button>
    </div>

    <template v-if="data">
      <!-- 基本信息 -->
      <div class="card-group">
        <div class="info-card">
          <div class="card-title">服务器信息</div>
          <div class="card-body">
            <div class="info-row"><span class="label">服务器名称</span><span class="value">{{ data.machineName }}</span></div>
            <div class="info-row"><span class="label">操作系统</span><span class="value">{{ data.osVersion }}</span></div>
            <div class="info-row"><span class="label">.NET 版本</span><span class="value">{{ data.dotNetVersion }}</span></div>
            <div class="info-row"><span class="label">CPU 核心</span><span class="value">{{ data.cpuCount }} 核</span></div>
            <div class="info-row"><span class="label">服务器时间</span><span class="value">{{ data.serverTime }}</span></div>
          </div>
        </div>
        <div class="info-card">
          <div class="card-title">进程资源</div>
          <div class="card-body">
            <div class="info-row"><span class="label">进程名称</span><span class="value">{{ data.processName }}</span></div>
            <div class="info-row"><span class="label">工作集内存</span><span class="value">{{ data.workingSetMB }} MB</span></div>
            <div class="info-row"><span class="label">私有内存</span><span class="value">{{ data.privateMemoryMB }} MB</span></div>
            <div class="info-row"><span class="label">线程数</span><span class="value">{{ data.threadCount }}</span></div>
            <div class="info-row"><span class="label">句柄数</span><span class="value">{{ data.handleCount }}</span></div>
            <div class="info-row"><span class="label">启动时间</span><span class="value">{{ data.startTime }}</span></div>
            <div class="info-row"><span class="label">运行时长</span><span class="value">{{ formatUptime(data.uptimeSeconds) }}</span></div>
          </div>
        </div>
        <div class="info-card">
          <div class="card-title">磁盘空间</div>
          <div class="card-body disk-section">
            <el-progress type="dashboard" :percentage="diskPercent()" :width="120" :stroke-width="12"
              :color="diskPercent() > 90 ? '#f56c6c' : diskPercent() > 70 ? '#e6a23c' : '#67c23a'" />
            <div class="disk-info">
              <div class="info-row"><span class="label">总容量</span><span class="value">{{ data.diskTotalGB }} GB</span></div>
              <div class="info-row"><span class="label">可用空间</span><span class="value">{{ data.diskFreeGB }} GB</span></div>
              <div class="info-row"><span class="label">已使用</span><span class="value">{{ data.diskTotalGB - data.diskFreeGB }} GB ({{ diskPercent() }}%)</span></div>
            </div>
          </div>
        </div>
      </div>

      <!-- Hangfire 任务统计 -->
      <div class="card-group">
        <div class="info-card wide">
          <div class="card-title">Hangfire 任务统计</div>
          <div class="card-body">
            <div class="stat-row">
              <div class="stat-item">
                <div class="stat-num">{{ data.hangfireEnqueued }}</div>
                <div class="stat-label">队列中</div>
              </div>
              <div class="stat-item">
                <div class="stat-num">{{ data.hangfireScheduled }}</div>
                <div class="stat-label">已计划</div>
              </div>
              <div class="stat-item">
                <div class="stat-num">{{ data.hangfireProcessing }}</div>
                <div class="stat-label">执行中</div>
              </div>
              <div class="stat-item success">
                <div class="stat-num">{{ data.hangfireSucceeded }}</div>
                <div class="stat-label">已成功</div>
              </div>
              <div class="stat-item danger">
                <div class="stat-num">{{ data.hangfireFailed }}</div>
                <div class="stat-label">已失败</div>
              </div>
              <div class="stat-item">
                <div class="stat-num">{{ data.hangfireRecurring }}</div>
                <div class="stat-label">周期任务</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Hangfire 服务器列表 -->
      <div class="card-group" v-if="data.hangfireServers.length > 0">
        <div class="info-card wide">
          <div class="card-title">Hangfire 服务器</div>
          <div class="card-body">
            <el-table :data="data.hangfireServers" border size="small">
              <el-table-column prop="name" label="服务器名称" />
              <el-table-column prop="workerCount" label="工作线程" width="100" />
              <el-table-column prop="startedAt" label="启动时间" width="180" />
              <el-table-column prop="heartbeat" label="最后心跳" width="180" />
            </el-table>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.dashboard-page {
  padding: 20px;
  height: 100%;
  overflow-y: auto;
}
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
}
.page-header h2 {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  margin: 0;
}
.card-group {
  display: flex;
  gap: 16px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}
.info-card {
  flex: 1;
  min-width: 280px;
  background: var(--surface, #fff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius, 12px);
  overflow: hidden;
}
.info-card.wide {
  min-width: 100%;
}
.card-title {
  padding: 12px 16px;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  border-bottom: 1px solid var(--border, #e2e8f0);
  background: var(--page-bg, #f8fafc);
}
.card-body {
  padding: 16px;
}
.info-row {
  display: flex;
  justify-content: space-between;
  padding: 6px 0;
  font-size: 13px;
}
.info-row .label {
  color: var(--text-sub, #64748b);
}
.info-row .value {
  color: var(--text-main, #0f172a);
  font-weight: 500;
}
.disk-section {
  display: flex;
  align-items: center;
  gap: 24px;
}
.disk-info {
  flex: 1;
}
.stat-row {
  display: flex;
  gap: 24px;
  flex-wrap: wrap;
}
.stat-item {
  flex: 1;
  min-width: 80px;
  text-align: center;
  padding: 12px 8px;
  border-radius: var(--radius-sm, 8px);
  background: var(--page-bg, #f8fafc);
}
.stat-item .stat-num {
  font-size: 24px;
  font-weight: 700;
  color: var(--brand, #3b82f6);
}
.stat-item.success .stat-num { color: #10b981; }
.stat-item.danger .stat-num { color: #ef4444; }
.stat-item .stat-label {
  font-size: 12px;
  color: var(--text-sub, #64748b);
  margin-top: 4px;
}
</style>
