<script setup lang="ts">
/**
 * 全局 AI 助手：顶栏触发按钮（需 ai-chat 权限）+ 右侧抽屉（左栏会话列表 + 右栏对话区）。
 * 用户消息头像用登录用户真实头像，无头像回退名字首字母（与顶栏一致）。
 * 状态全部来自 useAiStore（模块级单例，抽屉关闭重开不丢生成进度；
 * 流式 chunk 经 chat store 的 window 事件桥接，SignalR 断链时 store 自动轮询兜底）。
 * 消息渲染：AI 回复为头像 + 白卡片，``` 围栏代码块深色渲染 + 关键字高亮 + 复制；
 * 不引 markdown 库，其余按纯文本 pre-wrap。
 */
import { computed, nextTick, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import {
  Promotion, Plus, Delete, Loading, CircleCheckFilled, CircleCloseFilled, Operation,
  CopyDocument, RefreshRight, TrendCharts, Document, Search, Message, Tickets,
} from '@element-plus/icons-vue'
import { useAiStore } from '@/common/stores/ai'
import { useAuthStore } from '@/common/stores/auth'
import { getAiTasks, updateAiTaskStatus, type AiTask } from '@/common/api/ai'

const store = useAiStore()
const auth = useAuthStore()
const input = ref('')
const msgListEl = ref<HTMLElement>()
/** 无头像时的回退文字：名字首字母（与顶栏/聊天页一致） */
const avatarText = computed(() => (auth.displayName || auth.currentAccount || '').trim().slice(0, 1).toUpperCase())

function formatTime(iso: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  const now = new Date()
  const sameDay = d.toDateString() === now.toDateString()
  const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  return sameDay ? hm : `${d.getMonth() + 1}-${d.getDate()} ${hm}`
}

function onInputKeyup(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    doSend()
  }
}

async function doSend() {
  const text = input.value.trim()
  if (!text || store.sending.value) return
  input.value = ''
  await store.sendMessage(text)
}

function scrollToBottom() {
  nextTick(() => {
    const el = msgListEl.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

watch(() => store.messages.value.map(m => m.content.length).join(','), scrollToBottom)
watch(() => store.messages.value.length, scrollToBottom)

// ---------------------------------------------------------------- 内容渲染

/** 消息内容分段：``` 围栏代码块（流式未闭合也按代码渲染）+ 普通文本 */
interface Segment { type: 'text' | 'code'; text: string; lang: string }

function parseContent(content: string): Segment[] {
  if (!content) return []
  const segments: Segment[] = []
  const regex = /```(\w*)\r?\n([\s\S]*?)(?:```|$)/g
  let last = 0
  let m: RegExpExecArray | null
  while ((m = regex.exec(content))) {
    if (m.index > last) segments.push({ type: 'text', text: content.slice(last, m.index), lang: '' })
    segments.push({ type: 'code', text: m[2], lang: m[1] || '' })
    last = m.index + m[0].length
  }
  if (last < content.length) segments.push({ type: 'text', text: content.slice(last), lang: '' })
  return segments
}

/** 常见 SQL/C# 关键字高亮（先转义再包 span，v-html 安全） */
const CODE_KEYWORDS = /\b(select|from|where|and|or|order|by|group|having|join|left|right|inner|outer|on|as|count|sum|avg|max|min|distinct|top|case|when|then|else|end|null|is|not|in|between|like|union|all|insert|into|values|update|set|delete|if|else|for|foreach|while|return|var|new|public|private|class|void|task|async|await|string|int|bool|true|false)\b/gi

function highlightCode(code: string): string {
  const esc = code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  return esc.replace(CODE_KEYWORDS, '<span class="kw">$&</span>')
}

/** 复制文本（clipboard API 失败时降级 execCommand） */
async function copyText(text: string) {
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制')
  } catch {
    const ta = document.createElement('textarea')
    ta.value = text
    ta.style.position = 'fixed'
    ta.style.opacity = '0'
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success('已复制')
  }
}

// ---------------------------------------------------------------- 快捷指令

const quickCommands = [
  { icon: TrendCharts, color: '#409eff', title: '查询考勤', desc: '查询本月考勤记录', prompt: '帮我查询本月的考勤记录' },
  { icon: Document, color: '#67c23a', title: '写工作周报', desc: '一键生成周报草稿', prompt: '帮我写一份本周的工作周报' },
  { icon: Search, color: '#e6a23c', title: 'SQL 查询', desc: '自然语言生成 SQL', prompt: '帮我写一个查询考勤记录的 SQL' },
  { icon: Message, color: '#a855f7', title: '发送消息', desc: '给同事发站内信', prompt: '帮我发送一条站内消息' },
]

async function useQuick(prompt: string) {
  if (store.sending.value) return
  await store.sendMessage(prompt)
}

// ---------------------------------------------------------------- 工具调用

/** 工具名称标签 */
function getToolLabel(name: string): string {
  const labels: Record<string, string> = {
    send_message: '发送消息',
    query_attendance: '查询考勤',
    query_sql: 'SQL 查询',
    create_task: '创建任务',
  }
  return labels[name] || name
}

/** 工具状态文本 */
function getToolStatusText(status: string): string {
  const texts: Record<string, string> = {
    executing: '执行中…',
    done: '完成',
    error: '失败',
  }
  return texts[status] || status
}

// ---------------------------------------------------------------- AI 任务面板

const tasks = ref<AiTask[]>([])
const tasksLoading = ref(false)

const pendingTaskCount = computed(() => tasks.value.filter(t => t.status === 0).length)

/** 任务面板打开时拉取（el-popover @show） */
async function loadTasks() {
  tasksLoading.value = true
  try {
    tasks.value = await getAiTasks()
  } catch {
    /* httpGet silent，失败保持原列表 */
  } finally {
    tasksLoading.value = false
  }
}

/** 勾选切换：0 待办 ↔ 1 完成 */
async function toggleTask(t: AiTask) {
  const next = t.status === 1 ? 0 : 1
  try {
    await updateAiTaskStatus(t.id, next)
    t.status = next
  } catch {
    /* httpPost 已弹错误提示 */
  }
}

/** 截止日期显示（本年 月-日，跨年补年份） */
function formatDate(iso: string): string {
  const d = new Date(iso)
  const md = `${d.getMonth() + 1}-${d.getDate()}`
  return d.getFullYear() === new Date().getFullYear() ? md : `${d.getFullYear()}-${md}`
}
</script>

<template>
  <!-- 顶栏触发按钮（无 ai-chat 权限或 AI 未启用时不显示） -->
  <template v-if="$has('ai-chat') && store.status.value?.enabled">
    <el-button circle size="small" class="sidebar-toggle ai-fab" title="AI 助手" @click="store.toggle()">
      <svg class="ai-robot-icon" viewBox="0 0 24 24" width="16" height="16" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect x="4" y="7" width="16" height="12" rx="3.5" stroke="currentColor" stroke-width="1.8"/>
        <circle cx="9" cy="13" r="1.6" fill="currentColor"/>
        <circle cx="15" cy="13" r="1.6" fill="currentColor"/>
        <path d="M12 4v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
        <circle cx="12" cy="3" r="1.4" fill="currentColor"/>
        <path d="M2 12v3M22 12v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
      </svg>
    </el-button>

    <el-drawer
      v-model="store.drawerOpen.value"
      size="640px"
      :append-to-body="true"
      :close-on-click-modal="false"
      class="ai-drawer"
    >
      <!-- 头部：机器人图标 + 标题（slot 定制，贴合顶栏渐变风格） -->
      <template #header>
        <div class="drawer-header">
          <span class="drawer-header-icon">
            <svg viewBox="0 0 24 24" width="14" height="14" fill="none" xmlns="http://www.w3.org/2000/svg">
              <rect x="4" y="7" width="16" height="12" rx="3.5" stroke="currentColor" stroke-width="1.8"/>
              <circle cx="9" cy="13" r="1.6" fill="currentColor"/>
              <circle cx="15" cy="13" r="1.6" fill="currentColor"/>
              <path d="M12 4v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
              <circle cx="12" cy="3" r="1.4" fill="currentColor"/>
              <path d="M2 12v3M22 12v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </span>
          <span class="drawer-header-title">AI 助手</span>
          <!-- 任务面板入口（create_task 工具创建的待办，勾选完成） -->
          <el-popover placement="bottom-end" :width="340" trigger="click" popper-class="ai-task-popover" @show="loadTasks">
            <template #reference>
              <button class="task-entry" type="button">
                <el-icon :size="13"><Tickets /></el-icon>
                <span>任务</span>
              </button>
            </template>
            <div class="ai-task-panel">
              <div class="ai-task-head">
                <span class="ai-task-head-title">我的任务</span>
                <span class="ai-task-count">{{ pendingTaskCount }} 项待办</span>
              </div>
              <div v-loading="tasksLoading" class="ai-task-body">
                <div v-for="t in tasks" :key="t.id" class="ai-task-item" :class="{ done: t.status === 1 }">
                  <el-checkbox :model-value="t.status === 1" @change="toggleTask(t)" />
                  <div class="ai-task-main">
                    <div class="ai-task-item-title" :title="t.description || t.title">{{ t.title }}</div>
                    <div class="ai-task-meta">
                      <span v-if="t.dueDate">截止 {{ formatDate(t.dueDate) }} · </span>
                      <span>{{ formatTime(t.createTime) }}</span>
                    </div>
                  </div>
                </div>
                <div v-if="!tasksLoading && tasks.length === 0" class="ai-task-empty">
                  暂无任务 —— 对 AI 说「帮我建个任务：明天提醒我交周报」
                </div>
              </div>
            </div>
          </el-popover>
        </div>
      </template>

      <!-- 左栏：会话列表（点选切换；hover 显示删除） -->
      <aside class="conv-side">
        <el-button class="new-conv-btn" :icon="Plus" @click="store.newConversation()">新对话</el-button>
        <div class="conv-list">
          <div
            v-for="c in store.conversations.value"
            :key="c.id"
            class="conv-cell"
            :class="{ active: c.id === store.currentConversationId.value }"
            @click="store.openConversation(c.id)"
          >
            <div class="conv-cell-title">{{ c.title }}</div>
            <div class="conv-cell-foot">
              <span class="conv-cell-time">{{ formatTime(c.updateTime) }}</span>
              <el-icon class="conv-cell-del" @click.stop="store.removeConversation(c.id)"><Delete /></el-icon>
            </div>
          </div>
          <div v-if="store.conversations.value.length === 0" class="conv-empty">暂无历史会话</div>
        </div>
      </aside>

      <!-- 未启用提示 -->
      <div v-if="store.status.value && !store.status.value.enabled" class="state-banner">
        AI 功能未启用，请联系管理员在「系统配置 → AI 配置」中开启。
      </div>

      <!-- 消息区 -->
      <div ref="msgListEl" class="msg-list" v-loading="store.loadingMessages.value">
        <!-- 欢迎屏：快捷指令卡片 -->
        <div v-if="store.messages.value.length === 0" class="welcome">
          <div class="welcome-robot">
            <svg viewBox="0 0 24 24" width="40" height="40" fill="none" xmlns="http://www.w3.org/2000/svg">
              <rect x="4" y="7" width="16" height="12" rx="3.5" stroke="currentColor" stroke-width="1.6"/>
              <circle cx="9" cy="13" r="1.6" fill="currentColor"/>
              <circle cx="15" cy="13" r="1.6" fill="currentColor"/>
              <path d="M12 4v3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>
              <circle cx="12" cy="3" r="1.4" fill="currentColor"/>
              <path d="M2 12v3M22 12v3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>
            </svg>
          </div>
          <h3 class="welcome-title">你好，我是 AI 助手</h3>
          <p class="welcome-desc">可以帮你查考勤、写周报、生成 SQL、发消息…</p>
          <div class="quick-grid">
            <div v-for="q in quickCommands" :key="q.title" class="quick-card" @click="useQuick(q.prompt)">
              <div class="quick-icon" :style="{ background: q.color + '1a', color: q.color }">
                <el-icon :size="18"><component :is="q.icon" /></el-icon>
              </div>
              <div class="quick-text">
                <div class="quick-title">{{ q.title }}</div>
                <div class="quick-desc">{{ q.desc }}</div>
              </div>
            </div>
          </div>
        </div>

        <!-- 消息循环 -->
        <div
          v-for="m in store.messages.value"
          :key="m.id"
          class="msg-row"
          :class="m.role"
        >
          <!-- 用户消息：右侧头像 + 蓝色气泡 -->
          <template v-if="m.role === 'user'">
            <div class="msg-bubble user-bubble">{{ m.content }}</div>
            <el-avatar v-if="auth.avatar" :size="28" :src="auth.avatar" class="user-avatar-img" />
            <div v-else class="avatar user-avatar">{{ avatarText }}</div>
          </template>

          <!-- AI 消息：机器人头像 + 白色卡片 -->
          <template v-else>
            <div class="avatar ai-avatar">
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect x="4" y="7" width="16" height="12" rx="3.5" stroke="currentColor" stroke-width="1.8"/>
                <circle cx="9" cy="13" r="1.6" fill="currentColor"/>
                <circle cx="15" cy="13" r="1.6" fill="currentColor"/>
                <path d="M12 4v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
                <circle cx="12" cy="3" r="1.4" fill="currentColor"/>
                <path d="M2 12v3M22 12v3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
              </svg>
            </div>
            <div class="ai-card" :class="{ 'ai-card-error': m.status === 2 }">
              <!-- 生成中且尚无内容：思考动画 -->
              <div v-if="m.status === 0 && !m.content" class="msg-thinking">
                <span class="thinking-dots"><i></i><i></i><i></i></span>
                <span class="thinking-text">AI 正在思考…</span>
              </div>

              <!-- 内容分段渲染：代码块 / 文本 -->
              <template v-else>
                <template v-for="(seg, i) in parseContent(m.content)" :key="i">
                  <div v-if="seg.type === 'code'" class="code-block">
                    <div class="code-head">
                      <span class="code-lang">{{ seg.lang || '代码' }}</span>
                      <span class="code-copy" @click="copyText(seg.text)">
                        <el-icon :size="12"><CopyDocument /></el-icon>复制
                      </span>
                    </div>
                    <pre class="code-body" v-html="highlightCode(seg.text)" />
                  </div>
                  <div v-else-if="seg.text.trim()" class="msg-text">
                    {{ seg.text }}<span v-if="m.status === 0 && i === parseContent(m.content).length - 1" class="cursor" />
                  </div>
                </template>
                <div v-if="m.status === 0 && m.content && parseContent(m.content)[parseContent(m.content).length - 1]?.type === 'code'" class="cursor cursor-line" />
              </template>

              <!-- 失败 / 已停止 -->
              <div v-if="m.status === 2 && m.error" class="msg-err">{{ m.error }}</div>
              <div v-else-if="m.status === 3" class="msg-stopped">已停止</div>

              <!-- 操作行：复制 / 重新生成（终态显示） -->
              <div v-if="m.status !== 0 && (m.content || m.error)" class="msg-actions">
                <span class="msg-action" title="复制全文" @click="copyText(m.content)">
                  <el-icon :size="13"><CopyDocument /></el-icon>复制
                </span>
                <span v-if="!store.sending.value" class="msg-action" title="重新生成" @click="store.regenerate(m.id)">
                  <el-icon :size="13"><RefreshRight /></el-icon>重新生成
                </span>
              </div>
            </div>
          </template>
        </div>

        <!-- 工具调用状态：轻量横条 -->
        <div v-if="store.toolCalls.value.length > 0" class="tool-calls">
          <div v-for="tc in store.toolCalls.value" :key="tc.id" class="tool-bar" :class="tc.status">
            <el-icon v-if="tc.status === 'executing'" class="is-loading"><Loading /></el-icon>
            <el-icon v-else-if="tc.status === 'done'" color="#67c23a"><CircleCheckFilled /></el-icon>
            <el-icon v-else-if="tc.status === 'error'" color="#f56c6c"><CircleCloseFilled /></el-icon>
            <el-icon v-else><Operation /></el-icon>
            <span class="tool-name">{{ getToolLabel(tc.name) }}</span>
            <span class="tool-status">{{ getToolStatusText(tc.status) }}</span>
            <span v-if="tc.error" class="tool-error" :title="tc.error">{{ tc.error.slice(0, 40) }}</span>
            <el-tooltip v-else-if="tc.result" :content="tc.result" placement="top" :show-after="300">
              <span class="tool-result">{{ tc.result.slice(0, 40) }}</span>
            </el-tooltip>
          </div>
        </div>
      </div>

      <!-- 底部输入区 -->
      <div class="input-bar">
        <div class="quota" title="今日已用请求次数">今日已用 {{ store.quotaText.value }}</div>
        <div class="input-row">
          <el-input
            v-model="input"
            type="textarea"
            :rows="2"
            resize="none"
            class="chat-input"
            :disabled="store.sending.value || !store.canSend.value"
            placeholder="输入消息，Enter 发送 / Shift+Enter 换行"
            @keydown="onInputKeyup"
          />
          <el-button
            v-if="!store.sending.value"
            circle
            class="send-btn"
            :icon="Promotion"
            :disabled="!input.trim() || !store.canSend.value"
            @click="doSend()"
          />
          <el-button v-else type="danger" plain @click="store.stopGenerating()">停止</el-button>
        </div>
      </div>
    </el-drawer>
  </template>
</template>

<style scoped>
/* AI 机器人悬浮按钮：蓝紫渐变圆形背景 */
.ai-fab.sidebar-toggle {
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  border: none;
  color: #fff;
  box-shadow: 0 2px 8px rgba(139, 92, 246, 0.35);
  transition: transform 0.2s, box-shadow 0.2s;
}

.ai-fab.sidebar-toggle:hover {
  color: #fff;
  background: linear-gradient(135deg, #4f46e5 0%, #9333ea 100%);
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.5);
  transform: translateY(-1px);
}

.ai-robot-icon {
  display: block;
}

.drawer-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.drawer-header-icon {
  display: inline-flex;
  width: 26px;
  height: 26px;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  color: #fff;
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  box-shadow: 0 2px 6px rgba(139, 92, 246, 0.3);
}

.drawer-header-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

/* 头部「任务」入口（顶到右侧，紧邻抽屉关闭按钮） */
.task-entry {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--el-text-color-secondary);
  font-size: 12.5px;
  cursor: pointer;
  transition: color 0.15s, background 0.15s;
}

.task-entry:hover {
  color: #7c3aed;
  background: #f1edff;
}

/* ---------------- 左栏：会话列表 ---------------- */
.conv-side {
  grid-column: 1;
  grid-row: 1 / 4;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
  border-right: 1px solid var(--el-border-color-lighter);
  padding-right: 12px;
}

.new-conv-btn.el-button {
  width: 100%;
  height: 34px;
  border: none;
  color: #fff;
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  box-shadow: 0 2px 8px rgba(139, 92, 246, 0.3);
  transition: background 0.2s, box-shadow 0.2s;
}

.new-conv-btn.el-button:hover,
.new-conv-btn.el-button:focus {
  color: #fff;
  background: linear-gradient(135deg, #4f46e5 0%, #9333ea 100%);
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.5);
}

.conv-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.conv-cell {
  padding: 8px 10px;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s;
}

.conv-cell:hover {
  background: var(--el-fill-color-light);
}

.conv-cell.active {
  background: #f1edff;
}

.conv-cell-title {
  font-size: 13px;
  line-height: 1.4;
  color: var(--el-text-color-primary);
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.conv-cell.active .conv-cell-title {
  color: #7c3aed;
  font-weight: 600;
}

.conv-cell-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 4px;
}

.conv-cell-time {
  font-size: 11px;
  color: var(--el-text-color-secondary);
}

.conv-cell-del {
  font-size: 13px;
  color: var(--el-text-color-secondary);
  opacity: 0;
  transition: opacity 0.15s, color 0.15s;
}

.conv-cell:hover .conv-cell-del {
  opacity: 1;
}

.conv-cell-del:hover {
  color: var(--el-color-danger);
}

.conv-empty {
  padding: 24px 0;
  text-align: center;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.state-banner {
  grid-column: 2;
  grid-row: 1;
  padding: 10px 14px;
  border-radius: 8px;
  background: var(--el-color-warning-light-9);
  color: var(--el-text-color-regular);
  font-size: 13px;
}

.msg-list {
  grid-column: 2;
  grid-row: 2;
  min-height: 0;
  overflow-y: auto;
  padding: 12px 2px 4px 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* ---------------- 欢迎屏 ---------------- */
.welcome {
  margin: auto;
  text-align: center;
  padding: 24px 8px;
}

.welcome-robot {
  display: inline-flex;
  width: 64px;
  height: 64px;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  color: #fff;
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  box-shadow: 0 4px 16px rgba(139, 92, 246, 0.35);
}

.welcome-title {
  margin: 16px 0 4px;
  font-size: 18px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.welcome-desc {
  margin: 0 0 20px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.quick-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  text-align: left;
}

.quick-card {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 12px;
  cursor: pointer;
  transition: border-color 0.2s, box-shadow 0.2s, transform 0.2s;
}

.quick-card:hover {
  border-color: #c0b4fc;
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.12);
  transform: translateY(-1px);
}

.quick-icon {
  flex: none;
  width: 34px;
  height: 34px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 9px;
}

.quick-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.quick-desc {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}

/* ---------------- 消息布局 ---------------- */
.msg-row {
  display: flex;
  gap: 8px;
  align-items: flex-start;
}

.msg-row.user {
  justify-content: flex-end;
}

/* 头像 */
.avatar {
  flex: none;
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  margin-top: 2px;
}

.ai-avatar {
  color: #fff;
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  box-shadow: 0 2px 6px rgba(139, 92, 246, 0.3);
}

.user-avatar {
  color: #fff;
  background: var(--el-color-primary);
  box-shadow: 0 2px 6px rgba(64, 158, 255, 0.3);
  font-size: 13px;
  font-weight: 500;
}

/* 用户真实头像（data URL；无头像时回退上面的首字母块） */
.user-avatar-img {
  flex: none;
  margin-top: 2px;
}

/* 用户气泡 */
.msg-bubble.user-bubble {
  max-width: 78%;
  padding: 10px 14px;
  border-radius: 12px;
  border-bottom-right-radius: 4px;
  background: var(--el-color-primary);
  color: #fff;
  font-size: 14px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

/* AI 卡片 */
.ai-card {
  max-width: 84%;
  padding: 10px 14px;
  border-radius: 12px;
  border-bottom-left-radius: 4px;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04);
  font-size: 14px;
  line-height: 1.6;
}

.ai-card-error {
  background: var(--el-color-danger-light-9);
  border-color: var(--el-color-danger-light-7);
}

.msg-text {
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--el-text-color-primary);
}

/* ---------------- 代码块 ---------------- */
.code-block {
  margin: 8px 0;
  border-radius: 8px;
  overflow: hidden;
  background: #282c34;
}

.code-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 5px 12px;
  background: rgba(255, 255, 255, 0.06);
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.code-lang {
  font-size: 12px;
  color: #8b949e;
  font-family: 'Consolas', 'Monaco', monospace;
}

.code-copy {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  color: #8b949e;
  cursor: pointer;
  transition: color 0.2s;
}

.code-copy:hover {
  color: #fff;
}

.code-body {
  margin: 0;
  padding: 10px 12px;
  overflow-x: auto;
  font-size: 12.5px;
  line-height: 1.6;
  font-family: 'Consolas', 'Monaco', monospace;
  color: #abb2bf;
  white-space: pre;
}

.code-body :deep(.kw) {
  color: #c678dd;
  font-weight: 500;
}

/* ---------------- 生成中动效 ---------------- */
.cursor {
  display: inline-block;
  width: 8px;
  height: 15px;
  margin-left: 2px;
  vertical-align: text-bottom;
  background: var(--el-color-primary);
  animation: blink 1s step-start infinite;
}

.cursor-line {
  display: block;
  margin: 4px 0 0;
}

@keyframes blink {
  50% { opacity: 0; }
}

.msg-thinking {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.thinking-dots {
  display: inline-flex;
  gap: 4px;
}

.thinking-dots i {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #a855f7;
  animation: thinking-bounce 1.4s infinite ease-in-out both;
}

.thinking-dots i:nth-child(1) { animation-delay: -0.32s; }
.thinking-dots i:nth-child(2) { animation-delay: -0.16s; }

.thinking-text {
  font-weight: 500;
}

@keyframes thinking-bounce {
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
}

/* ---------------- 消息状态与操作 ---------------- */
.msg-err,
.msg-stopped {
  margin-top: 6px;
  font-size: 12px;
  color: var(--el-color-danger);
}

.msg-stopped {
  color: var(--el-text-color-secondary);
}

.msg-actions {
  display: flex;
  gap: 14px;
  margin-top: 8px;
  padding-top: 6px;
  border-top: 1px dashed var(--el-border-color-lighter);
}

.msg-action {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  cursor: pointer;
  transition: color 0.2s;
}

.msg-action:hover {
  color: var(--el-color-primary);
}

/* ---------------- 工具调用横条 ---------------- */
.tool-calls {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.tool-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-radius: 8px;
  background: #ecf5ff;
  border: 1px solid #d9ecff;
  font-size: 12.5px;
}

.tool-bar.done {
  background: #f0f9eb;
  border-color: #e1f3d8;
}

.tool-bar.error {
  background: #fef0f0;
  border-color: #fde2e2;
}

.tool-name {
  font-weight: 500;
  color: #303133;
}

.tool-status {
  color: #909399;
  flex: none;
}

.tool-result,
.tool-error {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  color: #606266;
}

.tool-error {
  color: #f56c6c;
}

/* ---------------- 输入区 ---------------- */
.input-bar {
  grid-column: 2;
  grid-row: 3;
  padding-top: 12px;
  border-top: 1px solid var(--el-border-color-light);
}

.quota {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-bottom: 6px;
  text-align: right;
}

.input-row {
  display: flex;
  gap: 10px;
  align-items: flex-end;
}

/* 渐变圆形发送按钮（与机器人按钮呼应） */
.send-btn {
  width: 36px;
  height: 36px;
  background: linear-gradient(135deg, #6366f1 0%, #a855f7 100%);
  border: none;
  color: #fff;
  box-shadow: 0 2px 8px rgba(139, 92, 246, 0.35);
  transition: transform 0.2s, box-shadow 0.2s;
}

.send-btn:not(:disabled):hover {
  color: #fff;
  background: linear-gradient(135deg, #4f46e5 0%, #9333ea 100%);
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.5);
  transform: translateY(-1px);
}

.send-btn:disabled {
  background: linear-gradient(135deg, #a5b4fc 0%, #d8b4fe 100%);
  box-shadow: none;
}
</style>

<style>
/* 抽屉 body 撑满做 grid 分栏（左 176px 会话列表 + 右对话区；非 scoped：el-drawer 渲染在 body 下） */
.ai-drawer .el-drawer__body {
  display: grid;
  grid-template-columns: 176px 1fr;
  grid-template-rows: auto 1fr auto;
  column-gap: 12px;
  overflow: hidden;
  padding: 12px 20px 16px;
}

/* AI 任务面板（el-popover 内容传送到 body，需全局样式；用 .ai-task-popover 前缀收敛作用域） */
.ai-task-popover.el-popover {
  padding: 0;
}

.ai-task-popover .ai-task-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.ai-task-popover .ai-task-head-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.ai-task-popover .ai-task-count {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.ai-task-popover .ai-task-body {
  max-height: 320px;
  overflow-y: auto;
  padding: 6px 8px;
}

.ai-task-popover .ai-task-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 6px;
  border-radius: 8px;
}

.ai-task-popover .ai-task-item:hover {
  background: var(--el-fill-color-light);
}

.ai-task-popover .ai-task-item.done .ai-task-item-title {
  color: var(--el-text-color-secondary);
  text-decoration: line-through;
}

.ai-task-popover .ai-task-item .el-checkbox {
  margin-right: 0;
  flex: none;
  height: auto;
}

.ai-task-popover .ai-task-main {
  flex: 1;
  min-width: 0;
}

.ai-task-popover .ai-task-item-title {
  font-size: 13px;
  line-height: 1.4;
  color: var(--el-text-color-primary);
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.ai-task-popover .ai-task-meta {
  margin-top: 2px;
  font-size: 11.5px;
  color: var(--el-text-color-secondary);
}

.ai-task-popover .ai-task-empty {
  padding: 24px 12px;
  text-align: center;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}
</style>
