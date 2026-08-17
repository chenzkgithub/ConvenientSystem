<script setup lang="ts">
import { onBeforeUnmount, onMounted, onActivated, onDeactivated, ref, shallowRef, computed, watch, markRaw } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Close } from '@element-plus/icons-vue'
import { useTabsStore } from '@/common/stores/tabs'
import { monaco, detectLanguage, languageOptions } from '@/common/monacoSetup'
import {
  isHostAvailable, initHostBridge, disposeHostBridge,
  hostOpenFile, hostSaveFile, hostSaveFileAs, hostOpenLocation,
} from '@/common/hostFileBridge'

// ---------- File System Access API 最小类型声明（TS 标准库暂未内置） ----------
interface FsFileHandle {
  readonly name: string
  getFile(): Promise<File>
  createWritable(): Promise<{ write(data: string): Promise<void>; close(): Promise<void> }>
}
interface FsWindow {
  showOpenFilePicker?(options?: unknown): Promise<FsFileHandle[]>
  showSaveFilePicker?(options?: unknown): Promise<FsFileHandle>
}
const fsWindow = window as unknown as FsWindow

// ---------- 编辑器文件标签（页面内多文件） ----------
interface EditorTab {
  id: string
  model: monaco.editor.ITextModel
  fileHandle: FsFileHandle | null
  filePath: string | null
  fileName: string
  language: string
  dirty: boolean
  viewState?: monaco.editor.ICodeEditorViewState
}

// ---------- 编辑器状态 ----------
const editorEl = ref<HTMLElement>()
const editor = shallowRef<monaco.editor.IStandaloneCodeEditor>()
const tabs = ref<EditorTab[]>([])
const activeTabId = ref('')
const dark = ref(false)

const activeTab = computed(() => tabs.value.find((t) => t.id === activeTabId.value) ?? null)

// ---------- 右键菜单 ----------
const ctxMenu = ref({ visible: false, x: 0, y: 0, tabId: '' })
const ctxTabIndex = computed(() => tabs.value.findIndex((t) => t.id === ctxMenu.value.tabId))
const canCloseRight = computed(() => ctxTabIndex.value >= 0 && ctxTabIndex.value < tabs.value.length - 1)
const canCloseLeft = computed(() => ctxTabIndex.value > 0)
const ctxTab = computed(() => tabs.value.find((t) => t.id === ctxMenu.value.tabId) ?? null)
// 已保存（有磁盘路径或文件句柄）即可启用；浏览器中无路径时点击会提示
const canOpenLocation = computed(() => !!ctxTab.value?.filePath || !!ctxTab.value?.fileHandle)

function onTabContextMenu(e: MouseEvent, tabId: string) {
  e.preventDefault()
  e.stopPropagation()
  ctxMenu.value = { visible: true, x: e.clientX, y: e.clientY, tabId }
}
function closeCtxMenu() {
  ctxMenu.value.visible = false
}

// ---------- 应用级标签页联动 ----------
const route = useRoute()
const tabsStore = useTabsStore()
const tabPath = route.fullPath

// 应用标签标题跟随当前文件名 + 脏标记
const appTabTitle = computed(() => {
  const tab = activeTab.value
  if (!tab) return '代码编辑器'
  return `${tab.dirty ? '● ' : ''}${tab.fileName}`
})
watch(appTabTitle, (title) => tabsStore.renameTab(tabPath, title))

// ---------- 页面内文件标签管理 ----------
let tabCounter = 0

/** 创建一个新的文件标签（含 Monaco Model），返回标签对象 */
function addTab(fileName: string, content: string, language: string, handle: FsFileHandle | null, filePath: string | null = null): EditorTab {
  const id = `tab-${++tabCounter}`
  const model = markRaw(monaco.editor.createModel(content, language))
  const tab: EditorTab = {
    id,
    model,
    fileHandle: handle ? markRaw(handle) : null,
    filePath,
    fileName,
    language,
    dirty: false,
  }
  tabs.value.push(tab)
  return tab
}

/** 切换当前编辑的文件标签（保存/恢复视图状态，切换 Monaco Model） */
function switchTab(id: string) {
  if (id === activeTabId.value) return
  // 保存当前标签的编辑器视图状态（光标位置、滚动等）
  if (activeTab.value) {
    activeTab.value.viewState = editor.value?.saveViewState() ?? undefined
  }
  activeTabId.value = id
  const tab = tabs.value.find((t) => t.id === id)
  if (tab) {
    editor.value?.setModel(tab.model)
    if (tab.viewState) editor.value?.restoreViewState(tab.viewState)
    editor.value?.focus()
  }
  saveEditorState()
}

/** 关闭文件标签，有未保存内容时先询问。最后一个标签关闭后自动创建空标签 */
async function closeTab(id: string) {
  const tab = tabs.value.find((t) => t.id === id)
  if (!tab) return

  if (tab.dirty) {
    try {
      await ElMessageBox.confirm(`「${tab.fileName}」尚未保存，是否保存后关闭？`, '未保存', {
        confirmButtonText: '保存并关闭',
        cancelButtonText: '不保存',
        distinguishCancelAndClose: true,
        type: 'warning',
      })
      await saveTab(tab)
      if (tab.dirty) return // 保存失败或另存为被取消时不关闭
    } catch (action) {
      // "不保存"直接关闭；×/ESC 取消关闭
      if (action !== 'cancel') return
    }
  }

  const idx = tabs.value.findIndex((t) => t.id === id)
  tab.model.dispose()
  tabs.value.splice(idx, 1)

  if (tabs.value.length === 0) {
    const newTab = addTab('未命名.txt', '', 'plaintext', null)
    switchTab(newTab.id)
  } else if (activeTabId.value === id) {
    const next = tabs.value[idx] || tabs.value[idx - 1]
    switchTab(next.id)
  }
  saveEditorState()
}

/** 批量关闭标签：有脏数据时统一询问，一次性关闭 */
async function closeTabsBatch(ids: string[]): Promise<void> {
  if (ids.length === 0) return
  const dirtyTabs = tabs.value.filter((t) => ids.includes(t.id) && t.dirty)
  if (dirtyTabs.length > 0) {
    const names = dirtyTabs.map((t) => t.fileName).join('、')
    try {
      await ElMessageBox.confirm(
        dirtyTabs.length === 1
          ? `「${names}」尚未保存，是否保存后关闭？`
          : `有 ${dirtyTabs.length} 个文件尚未保存（${names}），是否全部保存后关闭？`,
        '未保存',
        {
          confirmButtonText: '保存并关闭',
          cancelButtonText: '不保存',
          distinguishCancelAndClose: true,
          type: 'warning',
        },
      )
      for (const tab of dirtyTabs) await saveTab(tab)
      if (dirtyTabs.some((t) => t.dirty)) return // 有保存失败时不关闭
    } catch (action) {
      if (action !== 'cancel') return // ×/ESC 取消
    }
  }
  // 统一销毁并移除
  const idSet = new Set(ids)
  const remaining: EditorTab[] = []
  for (const tab of tabs.value) {
    if (idSet.has(tab.id)) tab.model.dispose()
    else remaining.push(tab)
  }
  tabs.value = remaining
  if (tabs.value.length === 0) {
    const newTab = addTab('未命名.txt', '', 'plaintext', null)
    switchTab(newTab.id)
  } else if (!tabs.value.find((t) => t.id === activeTabId.value)) {
    switchTab(tabs.value[0].id)
  }
  saveEditorState()
}

// ---------- 右键菜单动作 ----------
async function ctxCloseCurrent() {
  const id = ctxMenu.value.tabId
  closeCtxMenu()
  await closeTab(id)
}
async function ctxCloseRight() {
  if (!canCloseRight.value) { closeCtxMenu(); return }
  const ids = tabs.value.slice(ctxTabIndex.value + 1).map((t) => t.id)
  closeCtxMenu()
  await closeTabsBatch(ids)
}
async function ctxCloseLeft() {
  if (!canCloseLeft.value) { closeCtxMenu(); return }
  const ids = tabs.value.slice(0, ctxTabIndex.value).map((t) => t.id)
  closeCtxMenu()
  await closeTabsBatch(ids)
}
async function ctxCloseAll() {
  const ids = tabs.value.map((t) => t.id)
  closeCtxMenu()
  await closeTabsBatch(ids)
}

function ctxOpenLocation() {
  if (!canOpenLocation.value) { closeCtxMenu(); return }
  const path = ctxTab.value?.filePath
  closeCtxMenu()
  if (path) {
    hostOpenLocation(path)
  } else {
    // 浏览器环境：File System Access API 不暴露文件路径，无法打开所在位置
    ElMessage.warning('浏览器环境无法获取文件路径，请在桌面应用中使用此功能')
  }
}

// ---------- 持久化（localStorage） ----------
const STORAGE_KEY = 'codeEditor:state'

interface PersistedTab {
  id: string
  fileName: string
  content: string
  language: string
  dirty: boolean
  filePath: string | null
}

/** 序列化所有标签到 localStorage（文件句柄无法序列化，恢复后需另存为） */
function saveEditorState() {
  try {
    const data = {
      tabs: tabs.value.map((t) => ({
        id: t.id,
        fileName: t.fileName,
        content: t.model.getValue(),
        language: t.language,
        dirty: t.dirty,
        filePath: t.filePath,
      })),
      activeTabId: activeTabId.value,
      dark: dark.value,
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data))
  } catch {
    // 存储满或不可用时忽略
  }
}

/** 从 localStorage 恢复标签，返回是否成功恢复 */
function restoreEditorState(): boolean {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return false
    const data = JSON.parse(raw) as { tabs: PersistedTab[]; activeTabId: string; dark?: boolean }
    if (!Array.isArray(data.tabs) || data.tabs.length === 0) return false

    // 恢复主题
    if (data.dark) {
      dark.value = true
      monaco.editor.setTheme('vs-dark')
    }

    const idMap = new Map<string, string>()
    for (const saved of data.tabs) {
      const tab = addTab(saved.fileName, saved.content || '', saved.language || 'plaintext', null, saved.filePath || null)
      tab.dirty = !!saved.dirty
      idMap.set(saved.id, tab.id)
    }
    const activeId = idMap.get(data.activeTabId) || tabs.value[0]?.id || ''
    if (activeId) switchTab(activeId)
    return true
  } catch {
    return false
  }
}

// 内容变化时防抖保存（避免频繁写 localStorage）
let saveTimer: ReturnType<typeof setTimeout> | null = null
function debouncedSave() {
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(saveEditorState, 1000)
}

// ---------- 工具栏动作 ----------
function newFile() {
  const tab = addTab('未命名.txt', '', 'plaintext', null)
  switchTab(tab.id)
}

function setLanguage(lang: string) {
  if (!activeTab.value) return
  activeTab.value.language = lang
  monaco.editor.setModelLanguage(activeTab.value.model, lang)
}

function toggleTheme() {
  dark.value = !dark.value
  monaco.editor.setTheme(dark.value ? 'vs-dark' : 'vs')
}

async function openFile() {
  // 优先通过 C# 宿主打开文件（可获取磁盘路径，用于"打开文件所在位置"）
  if (isHostAvailable()) {
    const result = await hostOpenFile()
    if (result) await openContentLoaded(result.content, result.fileName, result.path, null)
    return
  }
  // 回退：File System Access API（开发环境浏览器）
  if (fsWindow.showOpenFilePicker) {
    try {
      const [handle] = await fsWindow.showOpenFilePicker()
      const file = await handle.getFile()
      await openContentLoaded(await file.text(), file.name, null, handle)
    } catch {
      // 用户取消选择时忽略
    }
    return
  }
  // 回退：普通文件选择（只读，保存走另存为下载）
  const input = document.createElement('input')
  input.type = 'file'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) void file.text().then((text) => openContentLoaded(text, file.name, null, null))
  }
  input.click()
}

/** 判断标签是否为空占位（未命名、无内容、无脏标记） */
function isPlaceholder(tab: EditorTab): boolean {
  return !tab.dirty && tab.fileName === '未命名.txt' && tab.model.getValue() === ''
}

/** 加载已读取的文件内容到编辑器 */
async function openContentLoaded(
  content: string,
  fileName: string,
  filePath: string | null,
  handle: FsFileHandle | null,
) {
  // 同名/同路径文件已打开则直接切换
  const existing = tabs.value.find((t) =>
    (filePath && t.filePath === filePath) ||
    (handle && t.fileHandle && t.fileHandle.name === handle.name),
  )
  if (existing) {
    switchTab(existing.id)
    return
  }

  try {
    const lang = detectLanguage(fileName)
    // 当前为空占位标签时替换内容，避免积累空标签
    const current = activeTab.value
    if (current && isPlaceholder(current)) {
      current.model.setValue(content)
      current.fileHandle = handle ? markRaw(handle) : null
      current.filePath = filePath
      current.fileName = fileName
      current.language = lang
      monaco.editor.setModelLanguage(current.model, lang)
      current.dirty = false
    } else {
      const tab = addTab(fileName, content, lang, handle, filePath)
      switchTab(tab.id)
    }
  } catch (e) {
    ElMessage.error('加载文件失败：' + (e as Error).message)
  }
}

// ---------- 拖拽打开文件 ----------
function onDragOver(e: DragEvent) {
  // 必须 preventDefault 才会触发 drop；capture + stopPropagation 覆盖 Monaco 自带的文本拖入行为。
  e.preventDefault()
  e.stopPropagation()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy'
}

async function onDrop(e: DragEvent) {
  e.preventDefault()
  e.stopPropagation()
  const dt = e.dataTransfer
  const file = dt?.files?.[0]
  if (!file) return
  // 必须在任何 await 之前同步发起取句柄（drop 事件结束后 DataTransfer 即失效）；
  // 拿到句柄才能直接回写原文件，否则保存时走另存为。
  const item = dt?.items?.[0] as
    | (DataTransferItem & { getAsFileSystemHandle?: () => Promise<{ kind?: string } | null> })
    | undefined
  const handlePromise = item?.getAsFileSystemHandle?.() ?? Promise.resolve(null)

  let handle: FsFileHandle | null = null
  try {
    const h = await handlePromise
    if (h && h.kind === 'file') handle = h as unknown as FsFileHandle
  } catch {
    // 取句柄失败时降级为只读打开
  }
  const text = await file.text()
  await openContentLoaded(text, file.name, null, handle)
}

// ---------- 保存 ----------
async function saveTab(tab: EditorTab) {
  // 优先通过 C# 宿主保存
  if (isHostAvailable()) {
    if (tab.filePath) {
      // 有磁盘路径：直接保存
      const result = await hostSaveFile(tab.filePath, tab.model.getValue())
      if (result) {
        tab.dirty = false
        ElMessage.success('已保存')
      }
      return
    }
    // 无磁盘路径：走另存为
    await saveTabAs(tab)
    return
  }
  // 回退：File System Access API
  if (!tab.fileHandle) {
    await saveTabAs(tab)
    return
  }
  try {
    const writable = await tab.fileHandle.createWritable()
    await writable.write(tab.model.getValue())
    await writable.close()
    tab.dirty = false
    ElMessage.success('已保存')
  } catch (e) {
    ElMessage.error('保存失败：' + (e as Error).message)
  }
}

async function saveTabAs(tab: EditorTab) {
  const content = tab.model.getValue()
  // 优先通过 C# 宿主另存为
  if (isHostAvailable()) {
    const result = await hostSaveFileAs(tab.fileName, content)
    if (result) {
      tab.filePath = result.path
      tab.fileHandle = null
      tab.fileName = result.fileName
      tab.language = detectLanguage(result.fileName)
      monaco.editor.setModelLanguage(tab.model, tab.language)
      tab.dirty = false
      ElMessage.success('已保存')
    }
    return
  }
  // 回退：File System Access API
  if (fsWindow.showSaveFilePicker) {
    try {
      const handle = await fsWindow.showSaveFilePicker({ suggestedName: tab.fileName })
      const writable = await handle.createWritable()
      await writable.write(content)
      await writable.close()
      tab.fileHandle = markRaw(handle)
      tab.fileName = handle.name
      tab.language = detectLanguage(handle.name)
      monaco.editor.setModelLanguage(tab.model, tab.language)
      tab.dirty = false
      ElMessage.success('已保存')
    } catch {
      // 用户取消保存时忽略
    }
    return
  }
  // 回退：以下载方式保存
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = tab.fileName
  a.click()
  URL.revokeObjectURL(url)
  tab.dirty = false
}

function saveFile() {
  if (activeTab.value) void saveTab(activeTab.value)
}

function saveFileAs() {
  if (activeTab.value) void saveTabAs(activeTab.value)
}

// ---------- 刷新拦截 ----------
/** 刷新/关闭窗口时仅持久化，不阻止离开（内容已保存到 localStorage，下次打开恢复） */
function onBeforeUnload() {
  saveEditorState()
}

// ---------- 生命周期 ----------
onMounted(() => {
  window.addEventListener('beforeunload', onBeforeUnload)
  window.addEventListener('click', closeCtxMenu)

  // 初始化 C# 宿主桥接（浏览器中自动跳过）
  initHostBridge((msg) => ElMessage.error(msg))

  if (!editorEl.value) return
  editor.value = monaco.editor.create(editorEl.value, {
    value: '',
    language: 'plaintext',
    theme: dark.value ? 'vs-dark' : 'vs',
    automaticLayout: true, // 容器尺寸变化（标签切换/窗口缩放）时自动重排
    fontSize: 14,
    minimap: { enabled: true },
    scrollBeyondLastLine: false,
    wordWrap: 'off',
  })

  // 内容变化时标记当前标签为脏 + 防抖持久化
  editor.value.onDidChangeModelContent(() => {
    if (activeTab.value) activeTab.value.dirty = true
    debouncedSave()
  })
  // Ctrl+S 保存当前文件
  editor.value.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
    if (activeTab.value) void saveTab(activeTab.value)
  })
  // Ctrl+N 新建文件
  editor.value.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyN, () => {
    newFile()
  })

  // 从 localStorage 恢复上次标签状态，无缓存时创建初始空标签
  if (!restoreEditorState()) {
    const tab = addTab('未命名.txt', '', 'plaintext', null)
    switchTab(tab.id)
  }
})

onBeforeUnmount(() => {
  saveEditorState() // 销毁前持久化，下次打开可恢复
  window.removeEventListener('beforeunload', onBeforeUnload)
  window.removeEventListener('click', closeCtxMenu)
  disposeHostBridge()
  tabs.value.forEach((t) => t.model.dispose())
  editor.value?.dispose()
  editor.value = undefined
})

// KeepAlive 离开时持久化（防止缓存淘汰后丢失）
onDeactivated(() => {
  saveEditorState()
  closeCtxMenu()
})

// KeepAlive 回归时刷新编辑器布局（容器尺寸可能已变化）
onActivated(() => {
  editor.value?.layout()
})
</script>

<template>
  <div class="code-editor-page" @dragover.capture="onDragOver" @drop.capture="onDrop">
    <div class="editor-toolbar">
      <el-button size="small" @click="newFile">新建</el-button>
      <el-button size="small" type="primary" @click="openFile">打开文件</el-button>
      <el-button size="small" type="success" :disabled="!activeTab?.dirty" @click="saveFile">保存</el-button>
      <el-button size="small" @click="saveFileAs">另存为</el-button>
      <el-select
        size="small"
        :model-value="activeTab?.language ?? 'plaintext'"
        filterable
        placeholder="语言"
        style="width: 130px; margin-left: 8px"
        @update:model-value="setLanguage"
      >
        <el-option v-for="opt in languageOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
      </el-select>
      <el-button size="small" style="margin-left: 8px" @click="toggleTheme">
        {{ dark ? '浅色主题' : '深色主题' }}
      </el-button>
    </div>
    <div class="editor-tabs">
      <div
        v-for="tab in tabs"
        :key="tab.id"
        class="editor-tab"
        :class="{ active: tab.id === activeTabId }"
        :title="tab.fileName"
        @click="switchTab(tab.id)"
        @contextmenu="onTabContextMenu($event, tab.id)"
      >
        <span class="editor-tab-name">{{ tab.fileName }}</span>
        <span v-if="tab.dirty" class="editor-tab-dirty">●</span>
        <el-icon class="editor-tab-close" @click.stop="closeTab(tab.id)">
          <Close />
        </el-icon>
      </div>
    </div>
    <div ref="editorEl" class="editor-container"></div>
    <!-- 右键菜单 -->
    <div
      v-if="ctxMenu.visible"
      class="ctx-menu"
      :style="{ left: ctxMenu.x + 'px', top: ctxMenu.y + 'px' }"
      @contextmenu.prevent
    >
      <div class="ctx-item" :class="{ disabled: !canOpenLocation }" @click="ctxOpenLocation">打开文件所在位置</div>
      <div class="ctx-separator"></div>
      <div class="ctx-item" @click="ctxCloseCurrent">关闭当前</div>
      <div class="ctx-item" :class="{ disabled: !canCloseRight }" @click="ctxCloseRight">关闭右边</div>
      <div class="ctx-item" :class="{ disabled: !canCloseLeft }" @click="ctxCloseLeft">关闭左边</div>
      <div class="ctx-item" @click="ctxCloseAll">全部关闭</div>
    </div>
  </div>
</template>

<style scoped>
.code-editor-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}
.editor-toolbar {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px 10px;
  border-bottom: 1px solid var(--el-border-color-light, #e4e7ed);
  flex: none;
}
.editor-tabs {
  display: flex;
  align-items: flex-end;
  gap: 1px;
  padding: 4px 8px 0;
  border-bottom: 1px solid var(--el-border-color-light, #e4e7ed);
  flex: none;
  overflow-x: auto;
  overflow-y: hidden;
}
.editor-tab {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  border-radius: 4px 4px 0 0;
  cursor: pointer;
  font-size: 13px;
  white-space: nowrap;
  color: var(--el-text-color-regular, #606266);
  border: 1px solid transparent;
  border-bottom: none;
  position: relative;
  top: 1px;
  user-select: none;
}
.editor-tab:hover {
  background: var(--el-fill-color-light, #f5f7fa);
}
.editor-tab.active {
  background: var(--el-bg-color, #fff);
  border-color: var(--el-border-color-light, #e4e7ed);
  color: var(--el-color-primary, #409eff);
  font-weight: 500;
}
.editor-tab-name {
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
}
.editor-tab-dirty {
  color: var(--el-color-warning, #e6a23c);
  font-size: 10px;
  flex: none;
}
.editor-tab-close {
  border-radius: 50%;
  padding: 1px;
  font-size: 12px;
  color: var(--el-text-color-secondary, #909399);
  flex: none;
}
.editor-tab-close:hover {
  background: var(--el-fill-color-dark, #dcdfe6);
  color: var(--el-text-color-primary, #303133);
}
.editor-container {
  flex: 1;
  min-height: 0;
}
.ctx-menu {
  position: fixed;
  z-index: 9999;
  min-width: 140px;
  background: var(--el-bg-color, #fff);
  border: 1px solid var(--el-border-color-light, #e4e7ed);
  border-radius: 4px;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.12);
  padding: 4px 0;
  user-select: none;
}
.ctx-item {
  padding: 6px 16px;
  font-size: 13px;
  cursor: pointer;
  color: var(--el-text-color-regular, #606266);
  white-space: nowrap;
}
.ctx-item:hover {
  background: var(--el-color-primary-light-9, #ecf5ff);
  color: var(--el-color-primary, #409eff);
}
.ctx-item.disabled {
  color: var(--el-text-color-placeholder, #a8abb2);
  cursor: not-allowed;
}
.ctx-item.disabled:hover {
  background: transparent;
  color: var(--el-text-color-placeholder, #a8abb2);
}
.ctx-separator {
  height: 1px;
  margin: 4px 0;
  background: var(--el-border-color-light, #e4e7ed);
}
</style>
