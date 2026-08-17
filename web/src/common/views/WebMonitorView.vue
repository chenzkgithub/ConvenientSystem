<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { httpGet, httpPost, httpDelete } from '@/api/request'
import { formatDate } from '@/common/formatDate'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import { confirmAndRun } from '@/common/utils/confirm'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

// ── 类型定义（与后端 WebMonitorModels.cs 对应） ──
interface MonitorTarget {
  id: number
  name: string
  url: string
  method: string
  expectStatus: number
  expectKeyword: string | null
  timeoutSeconds: number
  intervalMinutes: number
  enabled: boolean
  notifyEmail: boolean
  lastStatus: number | null   // null=未探测 1=正常 2=异常
  lastLatencyMs: number | null
  lastErrorMsg: string | null
  lastCheckAt: string | null
  remark: string | null
}

interface MonitorLog {
  id: number
  status: number
  httpStatusCode: number | null
  latencyMs: number | null
  errorMsg: string | null
  checkAt: string
}

// ── 监控目标列表 ──
const targets = ref<MonitorTarget[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    targets.value = await httpGet<MonitorTarget[]>('/api/Common/WebMonitor/List')
  } catch {
    targets.value = []
  } finally {
    loading.value = false
  }
}
load()

const columns = computed<DataTableColumn<MonitorTarget>[]>(() => [
  { type: 'index', label: '#', width: 50, align: 'center' },
  { prop: 'status', label: '状态', width: 90, align: 'center', custom: true },
  { prop: 'name', label: '目标名称', width: 200 },
  { prop: 'url', label: '监控地址', minWidth: 240, showOverflowTooltip: true, custom: true },
  { prop: 'method', label: '方式', width: 70, align: 'center' },
  { prop: 'expectStatus', label: '期望状态', width: 90, align: 'center' },
  { prop: 'intervalMinutes', label: '间隔(分)', width: 90, align: 'center' },
  {
    prop: 'lastLatencyMs', label: '耗时', width: 90, align: 'center',
    formatter: (row) => row.lastLatencyMs != null ? `${row.lastLatencyMs} ms` : '—',
  },
  {
    prop: 'lastCheckAt', label: '最近探测', width: 170, className: 'cell-nowrap',
    formatter: (row) => row.lastCheckAt ? formatDate(row.lastCheckAt) : '未探测',
  },
  { prop: 'enabled', label: '启用', width: 70, align: 'center', custom: true },
])

/** 状态标签：未探测 / 正常 / 异常（异常悬浮显示原因） */
function statusTag(row: MonitorTarget) {
  if (row.lastStatus == null) return { text: '未探测', type: 'info' as const }
  if (row.lastStatus === 1) return { text: '正常', type: 'success' as const }
  return { text: '异常', type: 'danger' as const }
}

// ── 新增 / 编辑 ──
const editVisible = ref(false)
const saving = ref(false)
const editFormRef = ref<FormInstance>()
interface EditForm {
  id: number | null
  name: string
  url: string
  method: string
  expectStatus: number
  expectKeyword: string
  timeoutSeconds: number
  intervalMinutes: number
  enabled: boolean
  notifyEmail: boolean
  remark: string
}
const editForm = ref<EditForm>(emptyForm())

function emptyForm(): EditForm {
  return {
    id: null, name: '', url: '', method: 'GET', expectStatus: 200,
    expectKeyword: '', timeoutSeconds: 10, intervalMinutes: 10,
    enabled: true, notifyEmail: true, remark: '',
  }
}

const editRules: FormRules = {
  name: [{ required: true, message: '请输入目标名称', trigger: 'blur' }],
  url: [{ required: true, message: '请输入监控地址', trigger: 'blur' }],
}

function openAdd() {
  editForm.value = emptyForm()
  editVisible.value = true
}

function openEdit(row: MonitorTarget) {
  editForm.value = {
    id: row.id, name: row.name, url: row.url, method: row.method,
    expectStatus: row.expectStatus, expectKeyword: row.expectKeyword ?? '',
    timeoutSeconds: row.timeoutSeconds, intervalMinutes: row.intervalMinutes,
    enabled: row.enabled, notifyEmail: row.notifyEmail, remark: row.remark ?? '',
  }
  editVisible.value = true
}

async function save() {
  if (!editFormRef.value) return
  await editFormRef.value.validate()
  saving.value = true
  try {
    await httpPost<number>('/api/Common/WebMonitor/Save', {
      id: editForm.value.id,
      name: editForm.value.name,
      url: editForm.value.url,
      method: editForm.value.method,
      expectStatus: editForm.value.expectStatus,
      expectKeyword: editForm.value.expectKeyword || null,
      timeoutSeconds: editForm.value.timeoutSeconds,
      intervalMinutes: editForm.value.intervalMinutes,
      enabled: editForm.value.enabled,
      notifyEmail: editForm.value.notifyEmail,
      remark: editForm.value.remark || null,
    })
    ElMessage.success(editForm.value.id ? '已保存' : '已添加')
    editVisible.value = false
    load()
  } finally {
    saving.value = false
  }
}

// ── 删除 ──
async function remove(row: MonitorTarget) {
  const ok = await confirmAndRun(
    `确定删除监控目标「${row.name}」及其探测日志吗？`,
    () => httpDelete(`/api/Common/WebMonitor/Delete?id=${row.id}`),
    { title: '确认删除', confirmButtonText: '删除' }
  )
  if (ok) load()
}

// ── 立即检测 ──
const checkingId = ref<number | null>(null)
async function checkNow(row: MonitorTarget) {
  checkingId.value = row.id
  try {
    const log = await httpPost<MonitorLog>(`/api/Common/WebMonitor/Check?id=${row.id}`, null)
    if (log.status === 1) {
      ElMessage.success(`「${row.name}」探测正常（${log.latencyMs} ms）`)
    } else {
      ElMessage.warning(`「${row.name}」探测异常：${log.errorMsg ?? '未知错误'}`)
    }
    load()
  } finally {
    checkingId.value = null
  }
}

// ── 探测日志弹窗 ──
const logVisible = ref(false)
const logTarget = ref<MonitorTarget | null>(null)
const logs = ref<MonitorLog[]>([])
const logTotal = ref(0)
const logPage = ref(1)
const logSize = ref(20)
const logLoading = ref(false)

const logColumns = computed<DataTableColumn<MonitorLog>[]>(() => [
  { type: 'index', label: '#', width: 50, align: 'center' },
  { prop: 'status', label: '结果', width: 80, align: 'center', custom: true },
  { prop: 'httpStatusCode', label: '状态码', width: 90, align: 'center', formatter: (row) => row.httpStatusCode != null ? String(row.httpStatusCode) : '—' },
  { prop: 'latencyMs', label: '耗时', width: 100, align: 'center', formatter: (row) => row.latencyMs != null ? `${row.latencyMs} ms` : '—' },
  { prop: 'errorMsg', label: '异常原因', minWidth: 200, showOverflowTooltip: true, formatter: (row) => row.errorMsg ?? '—' },
  { prop: 'checkAt', label: '探测时间', width: 170, className: 'cell-nowrap', formatter: (row) => formatDate(row.checkAt) },
])

function openLogs(row: MonitorTarget) {
  logTarget.value = row
  logPage.value = 1
  logVisible.value = true
  loadLogs()
}

async function loadLogs() {
  if (!logTarget.value) return
  logLoading.value = true
  try {
    const res = await httpGet<{ total: number; list: MonitorLog[] }>('/api/Common/WebMonitor/Logs', {
      targetId: logTarget.value.id, page: logPage.value, size: logSize.value,
    })
    logs.value = res.list
    logTotal.value = res.total
  } catch {
    logs.value = []
    logTotal.value = 0
  } finally {
    logLoading.value = false
  }
}
</script>

<template>
  <div class="monitor-page">
    <CommonDataTable
      :columns="columns"
      :data="targets"
      :loading="loading"
      :show-pagination="false"
      :actions-width="230"
      @load="load"
    >
      <template #toolbar>
        <el-button type="primary" size="small" @click="openAdd">新增监控</el-button>
        <el-button size="small" @click="load">刷新</el-button>
      </template>

      <template #cell-status="{ row }">
        <CommonTooltip
          v-if="(row as MonitorTarget).lastStatus === 2 && (row as MonitorTarget).lastErrorMsg"
          :content="(row as MonitorTarget).lastErrorMsg!"
        >
          <el-tag :type="statusTag(row as MonitorTarget).type" size="small" effect="dark">
            {{ statusTag(row as MonitorTarget).text }}
          </el-tag>
        </CommonTooltip>
        <el-tag v-else :type="statusTag(row as MonitorTarget).type" size="small" effect="dark">
          {{ statusTag(row as MonitorTarget).text }}
        </el-tag>
      </template>

      <template #cell-url="{ row }">
        <a :href="(row as MonitorTarget).url" target="_blank" rel="noopener" class="url-link">
          {{ (row as MonitorTarget).url }}
        </a>
      </template>

      <template #cell-enabled="{ row }">
        <el-tag v-if="(row as MonitorTarget).enabled" type="success" size="small" effect="plain">启用</el-tag>
        <el-tag v-else type="info" size="small" effect="plain">停用</el-tag>
      </template>

      <template #actions="{ row }">
        <el-button
          link type="primary" size="small"
          :loading="checkingId === (row as MonitorTarget).id"
          @click="checkNow(row as MonitorTarget)"
        >检测</el-button>
        <el-button link type="primary" size="small" @click="openLogs(row as MonitorTarget)">日志</el-button>
        <el-button link type="primary" size="small" @click="openEdit(row as MonitorTarget)">编辑</el-button>
        <el-button link type="danger" size="small" @click="remove(row as MonitorTarget)">删除</el-button>
      </template>

      <template #empty>暂无监控目标，点击"新增监控"添加</template>
    </CommonDataTable>

    <!-- 新增 / 编辑弹窗 -->
    <CommonDialog
      v-model="editVisible"
      :title="editForm.id ? '编辑监控目标' : '新增监控目标'"
      width="560px"
      destroy-on-close
    >
      <el-form ref="editFormRef" :model="editForm" :rules="editRules" label-width="110px">
        <el-form-item label="目标名称" prop="name">
          <el-input v-model="editForm.name" maxlength="100" placeholder="如：官网首页" />
        </el-form-item>
        <el-form-item label="监控地址" prop="url">
          <el-input v-model="editForm.url" placeholder="http(s)://..." />
        </el-form-item>
        <el-form-item label="请求方式">
          <el-select v-model="editForm.method" style="width: 120px">
            <el-option label="GET" value="GET" />
            <el-option label="POST" value="POST" />
            <el-option label="HEAD" value="HEAD" />
          </el-select>
        </el-form-item>
        <el-form-item label="期望状态码">
          <el-input-number v-model="editForm.expectStatus" :min="100" :max="599" :step="1" />
        </el-form-item>
        <el-form-item label="期望关键字">
          <el-input v-model="editForm.expectKeyword" maxlength="200" placeholder="选填：响应体需包含该文本才算正常" />
        </el-form-item>
        <el-form-item label="探测超时">
          <el-input-number v-model="editForm.timeoutSeconds" :min="1" :max="120" />
          <span class="unit-text">秒</span>
        </el-form-item>
        <el-form-item label="探测间隔">
          <el-input-number v-model="editForm.intervalMinutes" :min="1" :max="1440" />
          <span class="unit-text">分钟</span>
        </el-form-item>
        <el-form-item label="启用监控">
          <el-switch v-model="editForm.enabled" />
        </el-form-item>
        <el-form-item label="邮件告警">
          <el-switch v-model="editForm.notifyEmail" />
          <span class="unit-text">状态变化（正常↔异常）时邮件通知有网站监控权限的用户</span>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="editForm.remark" maxlength="200" placeholder="选填" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </CommonDialog>

    <!-- 探测日志弹窗 -->
    <CommonDialog
      v-model="logVisible"
      :title="`探测日志 · ${logTarget?.name ?? ''}`"
      width="760px"
      destroy-on-close
    >
      <CommonDataTable
        v-model:page="logPage"
        v-model:pageSize="logSize"
        :columns="logColumns"
        :data="logs"
        :loading="logLoading"
        :total="logTotal"
        :page-sizes="[20, 50, 100]"
        max-height="52vh"
        compact
        pagination-layout="total, sizes, prev, pager, next"
        @load="loadLogs"
      >
        <template #cell-status="{ row }">
          <el-tag v-if="(row as MonitorLog).status === 1" type="success" size="small" effect="dark">正常</el-tag>
          <el-tag v-else type="danger" size="small" effect="dark">异常</el-tag>
        </template>

        <template #empty>暂无探测记录</template>
      </CommonDataTable>
    </CommonDialog>
  </div>
</template>

<style scoped>
.monitor-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* 日期/时间列内容不换行，保证完整展示 */
:deep(.cell-nowrap .cell) {
  white-space: nowrap;
}

.url-link {
  color: #409eff;
  text-decoration: none;
  word-break: break-all;
}
.url-link:hover {
  text-decoration: underline;
}

.unit-text {
  margin-left: 8px;
  color: #909399;
  font-size: 12px;
}
</style>
