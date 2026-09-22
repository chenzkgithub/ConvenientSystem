<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, shallowRef, computed, nextTick } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { monaco, detectLanguage } from '@/common/monacoSetup'
import {
  getConfigFileList, readConfigFile, saveConfigFile,
  restartService, getConfigEditorStatus,
  type ConfigFileInfo,
} from '@/common/api/configEditor'

/** 文件列表 */
const files = ref<ConfigFileInfo[]>([])
/** 侧栏搜索关键字 */
const searchKeyword = ref('')
/** 当前选中文件名 */
const activeFile = ref('')
/** 编辑器原始内容（服务端返回的，用于 diff） */
const originalContent = ref('')
/** 编辑器当前内容（随 monaco 内容变化同步） */
const editContent = ref('')
/** 当前文件的最后修改时间 */
const fileModified = ref('')
/** 服务启动时间 */
const serviceStartTime = ref('')
/** 配置文件所在目录 */
const baseDirectory = ref('')
/** 读取文件加载中 */
const loading = ref(false)
/** 保存中 */
const saving = ref(false)
/** 重启中 */
const restarting = ref(false)
/** 保存确认对话框（mockup 风格：取消 / 仅保存 / 保存并重启） */
const saveDialogVisible = ref(false)
/** 光标位置（状态栏显示） */
const cursorPos = ref({ line: 1, column: 1 })

const editorEl = ref<HTMLElement>()
const editor = shallowRef<monaco.editor.IStandaloneCodeEditor>()
/** 当前文件的模型（每文件一个模型，切文件重建，互不影响撤销栈） */
let currentModel: monaco.editor.ITextModel | null = null

/** 是否有未保存更改 */
const hasChanges = computed(() => editContent.value !== originalContent.value)

/** 搜索过滤后的文件列表 */
const filteredFiles = computed(() => {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return files.value
  return files.value.filter(f => f.name.toLowerCase().includes(kw))
})

/** 文件类型图标（mockup 风格：彩色方块 + glyph） */
const TYPE_ICONS: Record<string, { glyph: string; color: string }> = {
  json: { glyph: '{ }', color: '#3b82f6' },
  env: { glyph: '=#', color: '#d48806' },
  conf: { glyph: '≡', color: '#389e0d' },
  yaml: { glyph: 'Y', color: '#722ed1' },
}
function typeIcon(type: string) {
  return TYPE_ICONS[type?.toLowerCase()] ?? { glyph: '≡', color: '#909399' }
}

onMounted(async () => {
  initEditor()
  await loadFileList()
  await loadStatus()
})

onBeforeUnmount(() => {
  editor.value?.dispose()
  currentModel?.dispose()
  currentModel = null
})

function initEditor() {
  if (!editorEl.value || editor.value) return
  editor.value = monaco.editor.create(editorEl.value, {
    value: '',
    language: 'json',
    theme: 'vs-dark',
    minimap: { enabled: false },
    automaticLayout: true,
    fontSize: 13,
    tabSize: 2,
    lineNumbers: 'on',
    renderLineHighlight: 'line',
    scrollBeyondLastLine: false,
    fontFamily: "'Cascadia Code', 'Fira Code', Consolas, monospace",
    padding: { top: 10, bottom: 12 },
    stickyScroll: { enabled: false },
    readOnly: true,
  })
  // 内容同步到 editContent，驱动未保存标记 / 保存按钮可用态
  editor.value.onDidChangeModelContent(() => {
    editContent.value = editor.value?.getValue() ?? ''
  })
  editor.value.onDidChangeCursorPosition(e => {
    cursorPos.value = { line: e.position.lineNumber, column: e.position.column }
  })
  editor.value.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
    openSaveDialog()
  })
}

async function loadFileList() {
  try {
    const res = await getConfigFileList()
    files.value = res ?? []
    if (files.value.length > 0 && !activeFile.value) {
      await selectFile(files.value[0].name)
    }
  } catch {
    ElMessage.error('加载配置文件列表失败')
  }
}

async function loadStatus() {
  try {
    const res = await getConfigEditorStatus()
    serviceStartTime.value = res?.startTime ?? ''
    baseDirectory.value = res?.baseDirectory ?? ''
  } catch { /* ignore */ }
}

/** 把文件内容装入编辑器（新模型，旧模型释放）；编辑器未创建时先补创建 */
function loadIntoEditor(content: string, fileName: string) {
  const ed = editor.value
  if (!ed) {
    nextTick(() => loadIntoEditor(content, fileName))
    return
  }
  currentModel?.dispose()
  currentModel = monaco.editor.createModel(content, detectLanguage(fileName))
  ed.setModel(currentModel)
  originalContent.value = content
  editContent.value = content
  ed.setPosition({ lineNumber: 1, column: 1 })
  ed.revealLineInCenter(1)
}

async function selectFile(name: string) {
  if (name === activeFile.value) return
  if (hasChanges.value) {
    try {
      await ElMessageBox.confirm('当前文件有未保存的更改，确定切换？', '提示', {
        confirmButtonText: '确定',
        cancelButtonText: '取消',
        type: 'warning',
      })
    } catch {
      return // 用户取消
    }
  }

  activeFile.value = name
  loading.value = true
  // 编辑器宿主在 v-if 内：首次进入页面时 onMounted 中 editorEl 尚未渲染，
  // 这里等 DOM 就绪后补创建，否则 loadIntoEditor 会因编辑器不存在而静默跳过
  await nextTick()
  initEditor()
  editor.value?.updateOptions({ readOnly: true })
  try {
    const data = await readConfigFile(name)
    fileModified.value = data.modified
    loadIntoEditor(data.content, name)
  } catch {
    ElMessage.error('读取文件失败')
  } finally {
    loading.value = false
    editor.value?.updateOptions({ readOnly: false })
  }
}

/** 保存入口：JSON 硬校验 + 打开确认对话框（替代旧版两步询问） */
function openSaveDialog() {
  if (!activeFile.value || !hasChanges.value || saving.value) return
  if (activeFile.value.toLowerCase().endsWith('.json')) {
    try {
      JSON.parse(editContent.value)
    } catch {
      ElMessage.error('JSON 格式错误，请检查后再保存（错误位置已在编辑器中标红）')
      return
    }
  }
  saveDialogVisible.value = true
}

/** 执行保存；withRestart 为 true 时保存成功后立即重启服务 */
async function doSave(withRestart: boolean) {
  if (!activeFile.value || saving.value) return
  saving.value = true
  try {
    await saveConfigFile(activeFile.value, editContent.value)
    originalContent.value = editContent.value
    saveDialogVisible.value = false
    ElMessage.success('保存成功')
    // 刷新文件列表（修改时间/大小可能变化）
    await loadFileList()

    if (withRestart) {
      restarting.value = true
      try {
        await restartService()
        ElMessage.success('服务正在重启，约 3-5 秒后恢复')
        // 重启完成后刷新启动时间（桌面端重启会重建进程，此处仅为云端场景兜底）
        setTimeout(loadStatus, 5000)
      } catch (e: any) {
        ElMessage.error(e?.message || '重启失败，请手动重启服务')
      } finally {
        restarting.value = false
      }
    }
  } catch (e: any) {
    ElMessage.error(e?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

/** 工具栏独立重启按钮 */
async function handleRestart() {
  try {
    await ElMessageBox.confirm('重启期间服务约 3-5 秒不可用，确定重启？', '重启服务', {
      confirmButtonText: '立即重启',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  restarting.value = true
  try {
    await restartService()
    ElMessage.success('服务正在重启，约 3-5 秒后恢复')
    setTimeout(loadStatus, 5000)
  } catch (e: any) {
    ElMessage.error(e?.message || '重启失败，请手动重启服务')
  } finally {
    restarting.value = false
  }
}

/** 重置：恢复为服务端最后一次保存的内容 */
function handleReset() {
  if (!hasChanges.value) return
  editor.value?.setValue(originalContent.value)
  ElMessage.success('已恢复到上次保存的内容')
}
</script>

<template>
  <div class="config-editor-page">
    <!-- 左侧文件列表 -->
    <aside class="file-sidebar">
      <div class="sidebar-head">
        <span>配置文件</span>
        <span class="count">{{ filteredFiles.length }}</span>
      </div>
      <div class="sidebar-search">
        <input
          v-model="searchKeyword"
          class="search-input"
          placeholder="搜索配置文件…"
          spellcheck="false"
        />
      </div>
      <div v-if="baseDirectory" class="sidebar-source" :title="baseDirectory">
        <span>📁</span>
        <span class="source-path">{{ baseDirectory }}</span>
      </div>
      <div class="file-list">
        <div
          v-for="f in filteredFiles"
          :key="f.name"
          class="file-item"
          :class="{ active: f.name === activeFile }"
          @click="selectFile(f.name)"
        >
          <span class="file-icon" :style="{ color: typeIcon(f.type).color, background: typeIcon(f.type).color + '1a' }">
            {{ typeIcon(f.type).glyph }}
          </span>
          <div class="file-info">
            <div class="file-name">
              {{ f.name }}
              <span v-if="f.name === activeFile && hasChanges" class="dirty-dot" title="有未保存的修改" />
            </div>
            <div class="file-meta">{{ f.modified }}</div>
          </div>
          <span class="file-size">{{ f.size }}</span>
        </div>
        <div v-if="filteredFiles.length === 0" class="empty-tip">
          {{ files.length === 0 ? '暂无可编辑的配置文件' : '无匹配的文件' }}
        </div>
      </div>
      <div class="sidebar-foot">共 {{ files.length }} 个文件</div>
    </aside>

    <!-- 右侧编辑器 -->
    <div class="editor-area">
      <!-- 顶部工具栏 -->
      <div class="editor-toolbar">
        <div class="toolbar-left">
          <template v-if="activeFile">
            <span class="crumb">配置中心</span>
            <span class="crumb-sep">/</span>
            <span class="crumb crumb-dir" :title="baseDirectory">{{ baseDirectory || '…' }}</span>
            <span class="crumb-sep">/</span>
            <span class="crumb crumb-cur">{{ activeFile }}</span>
            <span class="mtime">最后修改: {{ fileModified }}</span>
          </template>
          <span v-else class="crumb">配置中心</span>
        </div>
        <div class="toolbar-right">
          <el-button size="small" :disabled="!hasChanges" @click="handleReset">重 置</el-button>
          <el-button size="small" type="primary" :disabled="!hasChanges || !activeFile" @click="openSaveDialog">
            保 存 <span class="kbd">Ctrl+S</span>
          </el-button>
          <el-button size="small" type="danger" plain :loading="restarting" @click="handleRestart">重启服务</el-button>
        </div>
      </div>

      <!-- 编辑器装饰条（红绿灯 + 文件名） -->
      <div v-if="activeFile" class="editor-chrome">
        <span class="chrome-dot red" />
        <span class="chrome-dot yellow" />
        <span class="chrome-dot green" />
        <span class="chrome-title">{{ activeFile }} — {{ baseDirectory }}</span>
      </div>

      <!-- 代码编辑区（Monaco） -->
      <div v-if="activeFile" v-loading="loading" class="monaco-host" element-loading-background="rgba(30,30,30,.6)">
        <div ref="editorEl" class="monaco-el" />
      </div>
      <div v-else class="no-file-selected">
        <span>← 请从左侧选择一个配置文件</span>
      </div>

      <!-- 底部状态栏 -->
      <div class="editor-statusbar">
        <div class="status-left">
          <span class="save-indicator" :class="{ saved: !hasChanges, modified: hasChanges }">
            ● {{ hasChanges ? '未保存更改' : '已保存' }}
          </span>
          <span class="separator">|</span>
          <span class="cursor-pos">行 {{ cursorPos.line }}, 列 {{ cursorPos.column }}</span>
          <span class="separator">|</span>
          <span class="file-format">{{ activeFile?.split('.').pop()?.toUpperCase() || '-' }} · UTF-8</span>
        </div>
        <div class="status-right">
          <span v-if="serviceStartTime">服务启动: {{ serviceStartTime }}</span>
        </div>
      </div>
    </div>

    <!-- 保存确认对话框（取消 / 仅保存 / 保存并重启） -->
    <el-dialog
      v-model="saveDialogVisible"
      title="确认保存并重启服务？"
      width="440px"
      :close-on-click-modal="false"
      :append-to-body="true"
    >
      <p class="save-dialog-text">当前配置将在保存后立即生效</p>
      <p class="save-dialog-sub">「保存并重启」可使配置立即生效；「仅保存」留待下次重启后生效</p>
      <template #footer>
        <el-button :disabled="saving" @click="saveDialogVisible = false">取 消</el-button>
        <el-button :loading="saving && !restarting" :disabled="saving" @click="doSave(false)">仅保存</el-button>
        <el-button type="primary" :loading="saving" :disabled="saving" @click="doSave(true)">保存并重启</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.config-editor-page {
  display: flex;
  height: 100%;
  background: #f5f7fa;
  overflow: hidden;
}

/* ===== 左侧文件列表 ===== */
.file-sidebar {
  width: 280px;
  min-width: 280px;
  border-right: 1px solid #e4e7ed;
  background: #fafbfc;
  display: flex;
  flex-direction: column;
}

.sidebar-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px 10px;
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}

.count {
  background: #eef2f7;
  color: #606266;
  border-radius: 10px;
  font-size: 11px;
  padding: 1px 8px;
  font-weight: 400;
}

.sidebar-search {
  padding: 0 12px 10px;
}

.search-input {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  padding: 6px 10px;
  font-size: 12px;
  color: #606266;
  outline: none;
  background: #fff;
  transition: border-color .15s;
}

.search-input:focus {
  border-color: #3b82f6;
}

.search-input::placeholder {
  color: #c0c4cc;
}

.sidebar-source {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 12px;
  color: #909399;
  border-bottom: 1px solid #f0f0f0;
  overflow: hidden;
}

.source-path {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-list {
  flex: 1;
  overflow-y: auto;
  padding: 6px 8px;
}

.file-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 10px;
  cursor: pointer;
  border-radius: 6px;
  border-left: 3px solid transparent;
  transition: background .15s;
}

.file-item:hover {
  background: #f0f4fa;
}

.file-item.active {
  background: #e8f1fe;
  border-left-color: #3b82f6;
}

.file-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  font-size: 11px;
  font-weight: 700;
  font-family: Consolas, monospace;
  flex-shrink: 0;
}

.file-info {
  flex: 1;
  min-width: 0;
}

.file-name {
  font-size: 13px;
  font-weight: 500;
  color: #303133;
  display: flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-item.active .file-name {
  color: #3b82f6;
  font-weight: 600;
}

.dirty-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #e6a23c;
  flex-shrink: 0;
}

.file-meta {
  font-size: 11px;
  color: #909399;
  margin-top: 2px;
}

.file-size {
  font-size: 11px;
  color: #c0c4cc;
  flex-shrink: 0;
}

.empty-tip {
  padding: 40px 16px;
  text-align: center;
  color: #c0c4cc;
  font-size: 13px;
}

.sidebar-foot {
  padding: 10px 16px;
  border-top: 1px solid #ebeef2;
  font-size: 11px;
  color: #909399;
}

/* ===== 右侧编辑器 ===== */
.editor-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: #fff;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
  flex-wrap: wrap;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-size: 13px;
}

.crumb {
  color: #606266;
  white-space: nowrap;
}

.crumb-sep {
  color: #c0c4cc;
}

.crumb-dir {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.crumb-cur {
  color: #303133;
  font-weight: 600;
}

.mtime {
  margin-left: 8px;
  font-size: 12px;
  color: #909399;
  white-space: nowrap;
}

.toolbar-right {
  display: flex;
  gap: 8px;
}

.kbd {
  font-family: Consolas, monospace;
  font-size: 10px;
  background: rgba(255, 255, 255, .22);
  border: 1px solid rgba(255, 255, 255, .45);
  border-radius: 4px;
  padding: 1px 5px;
}

/* 编辑器装饰条 */
.editor-chrome {
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 8px 14px;
  background: #252526;
  border-bottom: 1px solid #1b1b1c;
  flex-shrink: 0;
}

.chrome-dot {
  width: 11px;
  height: 11px;
  border-radius: 50%;
  flex-shrink: 0;
}

.chrome-dot.red { background: #f56c6c; }
.chrome-dot.yellow { background: #e6a23c; }
.chrome-dot.green { background: #389e0d; }

.chrome-title {
  margin-left: 8px;
  font-size: 12px;
  color: #969696;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Monaco 宿主 */
.monaco-host {
  flex: 1;
  min-height: 0;
  position: relative;
}

.monaco-el {
  position: absolute;
  inset: 0;
}

.no-file-selected {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #909399;
  font-size: 14px;
  background: #fafafa;
}

/* ===== 底部状态栏 ===== */
.editor-statusbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 16px;
  border-top: 1px solid #e4e7ed;
  font-size: 12px;
  flex-shrink: 0;
}

.status-left,
.status-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.save-indicator.saved {
  color: #67c23a;
}

.save-indicator.modified {
  color: #e6a23c;
}

.separator {
  color: #dcdfe6;
}

.cursor-pos,
.file-format {
  color: #606266;
}

.status-right {
  color: #909399;
}

/* ===== 保存对话框 ===== */
.save-dialog-text {
  margin: 0;
  font-size: 13px;
  color: #606266;
}

.save-dialog-sub {
  margin: 8px 0 0;
  font-size: 12px;
  color: #909399;
}
</style>
