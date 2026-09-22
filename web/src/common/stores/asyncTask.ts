import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { ElNotification } from 'element-plus'
import { useAuthStore } from '@/common/stores/auth'
import {
  getAllAsyncTasks,
  getApifoxTaskProgress,
  getApifoxTaskResult,
  getAsyncResult,
  getAsyncTask,
  type AsyncTaskDto,
} from '@/common/api/asyncTask'

/** 轮询间隔（ms）：桌面本地任务无推送全靠轮询；云端任务有 SignalR 推送，收到推送短时间内跳过轮询 */
const POLL_INTERVAL = 2_000
/** SignalR 推送到达后的轮询抑制窗口（ms） */
const PUSH_SUPPRESS = 2_500
/** 轮询连续失败阈值：超过视为任务丢失（服务重启/被清理），标记失败提醒人工核对 */
const MAX_MISS = 15
/** localStorage 持久化的任务条数上限（防膨胀） */
const MAX_PERSIST = 50
const STORAGE_KEY = 'asyncTask.ids'

/** 持久化条目：仅存 id+kind，进度与结果刷新后按 id 回查 */
interface PersistedTask { id: string; kind: string }

function loadPersisted(): PersistedTask[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const list = raw ? (JSON.parse(raw) as PersistedTask[]) : []
    return Array.isArray(list) ? list.filter(x => x && typeof x.id === 'string') : []
  } catch {
    return []
  }
}

/**
 * 统一异步任务中心（前端）：提交/跟踪全部后台耗时任务，顶栏 AsyncTaskBell 展示实时进度。
 * 数据流：启动接口返回初始快照 → submit 纳入跟踪 → 轮询 + SignalR 推送（asyncTask:progress
 * window 事件，由 chat store 桥接）双通道更新。跨宿主任务合并展示：本地任务（apispec-*）
 * 走 AsyncTask 端点，云端任务（apifox-*）走 ApifoxConfig 端点（桌面代理模式下自动转发到云）。
 */
export const useAsyncTaskStore = defineStore('asyncTask', () => {
  const authStore = useAuthStore()

  /** 全部已知任务（key = taskId；含运行中与完成未清理，保留期内可查看结果） */
  const tasks = ref(new Map<string, AsyncTaskDto>())

  /** 展示列表：按开始时间倒序（新任务在上） */
  const list = computed(() => [...tasks.value.values()].sort((a, b) => b.startedAt.localeCompare(a.startedAt)))
  /** 运行中任务数（顶栏角标） */
  const runningCount = computed(() => list.value.filter(t => t.status === 'running').length)

  // ==================== 内部状态（非响应式） ====================

  let pollTimer: number | null = null
  /** 轮询去重：正在查询中的任务 */
  const inFlight = new Set<string>()
  /** 每任务最近一次 SignalR 推送时间（抑制重复轮询） */
  const lastPushAt = new Map<string, number>()
  /** 每任务连续查询失败次数（达阈值标记丢失） */
  const missCount = new Map<string, number>()
  let windowBound = false

  // ==================== 提交与更新 ====================

  /** 发起后台任务并纳入跟踪：starter 调启动接口返回任务初始快照；启动失败（并发拒绝等）直接抛给调用方提示 */
  async function submit(starter: () => Promise<AsyncTaskDto>): Promise<AsyncTaskDto> {
    const initial = await starter()
    upsert(initial)
    return initial
  }

  /** 更新任务快照（启动返回 / 轮询结果 / SignalR 推送）；有运行中任务即激活轮询，本会话内 running→终态时弹完成通知 */
  function upsert(task: AsyncTaskDto, fromPush = false) {
    if (!task?.taskId) return
    // 字段兜底：异常来源（代理截断/序列化缺失）的条目给默认值，避免任务面板渲染出空标题/空状态
    if (typeof task.title !== 'string' || !task.title) task.title = task.kind || '后台任务'
    if (task.status !== 'running' && task.status !== 'succeeded' && task.status !== 'failed') task.status = 'running'
    if (typeof task.startedAt !== 'string') task.startedAt = ''
    const prev = tasks.value.get(task.taskId)
    tasks.value.set(task.taskId, task)
    if (fromPush) lastPushAt.set(task.taskId, Date.now())
    if (task.status === 'running') ensurePolling() // 轮询会在全部终态后停表，新任务提交/推送恢复时重新激活
    if (prev && prev.status === 'running' && task.status !== 'running') notifyDone(task)
    persist()
  }

  function getTask(taskId: string): AsyncTaskDto | undefined {
    return tasks.value.get(taskId)
  }

  /** 清空已完成任务（运行中保留） */
  function removeFinished() {
    for (const t of [...tasks.value.values()]) if (t.status !== 'running') tasks.value.delete(t.taskId)
    persist()
  }

  // ==================== 轮询（推送不可用/未到达时兜底） ====================

  function ensurePolling() {
    if (pollTimer != null) return
    if (!runningTasks().length) return
    pollTimer = window.setInterval(() => void pollOnce(), POLL_INTERVAL)
  }

  function stopPolling() {
    if (pollTimer != null) {
      window.clearInterval(pollTimer)
      pollTimer = null
    }
  }

  function runningTasks() {
    return [...tasks.value.values()].filter(t => t.status === 'running')
  }

  /** 轮询所有运行中任务（kind 路由端点）；全部终态后自动停表 */
  async function pollOnce() {
    const running = runningTasks()
    if (running.length === 0) {
      stopPolling()
      return
    }
    const now = Date.now()
    for (const t of running) {
      if (inFlight.has(t.taskId)) continue
      // SignalR 推送到达不久的任务跳过本轮（云端有推送；桌面本地任务无推送不会被跳过）
      const pushed = lastPushAt.get(t.taskId)
      if (pushed && now - pushed < PUSH_SUPPRESS) continue
      inFlight.add(t.taskId)
      void queryTask(t.taskId, t.kind)
        .then(r => {
          if (r) {
            missCount.delete(t.taskId)
            upsert(r)
          } else {
            handleMiss(t)
          }
        })
        .finally(() => inFlight.delete(t.taskId))
    }
  }

  /** 查询连续失败（任务被清理/服务重启失联）达阈值：标记失败提醒人工核对实际执行结果 */
  function handleMiss(task: AsyncTaskDto) {
    const n = (missCount.get(task.taskId) ?? 0) + 1
    if (n < MAX_MISS) {
      missCount.set(task.taskId, n)
      return
    }
    missCount.delete(task.taskId)
    tasks.value.set(task.taskId, {
      ...task,
      status: 'failed' as const,
      error: '任务状态已丢失（服务可能已重启或任务被清理），请人工核对实际执行结果',
      finishedAt: new Date().toISOString(),
    })
    persist()
  }

  /** 按任务类型路由查询端点：apifox-* 云端任务 / 其他本地任务；失败返回 null（静默，下轮再查）。
   *  进度快照不含 result（大负载）：轮询/推送/恢复通道只发进度与终态摘要，页面终态后经 fetchResult 按需取完整结果 */
  async function queryTask(taskId: string, kind: string): Promise<AsyncTaskDto | null> {
    try {
      return kind.startsWith('apifox-')
        ? await getApifoxTaskProgress(taskId)
        : await getAsyncTask(taskId)
    } catch {
      return null
    }
  }

  /** 拉取已完成任务的完整 result（终态后按需调用）：按 kind 路由端点；结果不写入 store（避免大对象深代理），
   *  直接返回给调用方使用；失败返回 undefined 由调用方提示 */
  async function fetchResult(taskId: string, kind: string): Promise<AsyncTaskDto | undefined> {
    try {
      return kind.startsWith('apifox-')
        ? await getApifoxTaskResult(taskId)
        : await getAsyncResult(taskId)
    } catch {
      return undefined
    }
  }

  // ==================== 完成通知 ====================

  function notifyDone(task: AsyncTaskDto) {
    if (task.status === 'failed') {
      ElNotification({
        title: `${task.title}失败`,
        message: task.error || '任务执行失败',
        type: 'error',
        duration: 6_000,
      })
      return
    }
    ElNotification({
      title: `${task.title}完成`,
      message: summarize(task),
      type: 'success',
      duration: 4_500,
    })
  }

  /** 各类型完成摘要（通知与任务卡片副标题用）：后端终态填好的轻量摘要文本，无需拉取大 result */
  function summarize(task: AsyncTaskDto): string {
    return task.summary || ''
  }

  // ==================== 持久化与恢复 ====================

  /** 持久化任务 id 清单（新任务在前，超量截断），刷新后按 id 回查恢复 */
  function persist() {
    const ids: PersistedTask[] = list.value.slice(0, MAX_PERSIST).map(t => ({ id: t.taskId, kind: t.kind }))
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(ids))
    } catch {
      /* 隐私模式等存储不可用时忽略 */
    }
  }

  /** 页面刷新后恢复任务面板（MainLayout 挂载时调用）：localStorage 清单逐个回查 + 本地 GetAll 兜底（多标签等场景） */
  async function restore() {
    bindWindowEvents()
    for (const p of loadPersisted()) {
      const t = await queryTask(p.id, p.kind)
      if (t) upsert(t) // 已过期/查询失败：不恢复，下次 persist 自然清除
    }
    try {
      const local = await getAllAsyncTasks()
      for (const t of local ?? []) upsert(t)
    } catch {
      /* 本地端点不可用（纯云端浏览器模式无本地任务）时忽略 */
    }
    ensurePolling()
  }

  // ==================== SignalR 推送桥接 ====================

  /** chat store 转发的 asyncTask:progress window 事件（云端宿主有推送；桌面本地任务无推送靠轮询） */
  function bindWindowEvents() {
    if (windowBound) return
    windowBound = true
    window.addEventListener('asyncTask:progress', e => {
      const task = (e as CustomEvent<AsyncTaskDto>).detail
      if (task?.taskId) upsert(task, true)
    })
  }

  // ==================== 登出清理 ====================

  watch(() => authStore.loggedIn, v => {
    if (!v) {
      stopPolling()
      tasks.value.clear()
      lastPushAt.clear()
      missCount.clear()
      try {
        localStorage.removeItem(STORAGE_KEY)
      } catch {
        /* 忽略 */
      }
    }
  })

  return { tasks, list, runningCount, submit, getTask, upsert, removeFinished, restore, fetchResult, summarize }
})