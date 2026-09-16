<script setup lang="ts">
import { onMounted, ref, computed, watch, nextTick } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  getConfigFileList, readConfigFile, saveConfigFile,
  restartService, getConfigEditorStatus,
  type ConfigFileInfo,
} from '@/common/api/configEditor'

/** 文件列表 */
const files = ref<ConfigFileInfo[]>([])
/** 当前选中文件名 */
const activeFile = ref('')
/** 编辑器原始内容（服务端返回的，用于 diff） */
const originalContent = ref('')
/** 编辑器当前内容 */
const editContent = ref('')
/** 当前文件的最后修改时间 */
const fileModified = ref('')
/** 服务启动时间 */
const serviceStartTime = ref('')
/** 配置文件所在目录 */
const baseDirectory = ref('')
/** 加载中 */
const loading = ref(false)
/** 保存中 */
const saving = ref(false)
/** textarea ref */
const editorRef = ref<HTMLTextAreaElement | null>(null)

/** 是否有未保存更改 */
const hasChanges = computed(() => editContent.value !== originalContent.value)

/** 编辑器行数（用于左侧行号 gutter） */
const lineCount = computed(() => {
  const text = editContent.value
  if (!text) return 1
  let count = 0
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '\n') count++
  }
  return count + 1
})

/** 文件类型图标颜色映射 */
const typeColors: Record<string, string> = {
  json: '#f5a623',
  env: '#4caf50',
  conf: '#9c27b0',
  yaml: '#e91e63',
}

onMounted(async () => {
  await loadFileList()
  await loadStatus()
})

async function loadFileList() {
  try {
    // httpGet 泛型即返回体本身（拦截器已解包），无需再取 .data
    const res = await getConfigFileList()
    files.value = res ?? []
    // 默认选中第一个
    if (files.value.length > 0 && !activeFile.value) {
      await selectFile(files.value[0].name)
    }
  } catch (e) {
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

async function selectFile(name: string) {
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
  try {
    const data = await readConfigFile(name)
    originalContent.value = data.content
    editContent.value = data.content
    fileModified.value = data.modified
    // 等待 DOM 更新后滚动到顶部
    await nextTick()
    if (editorRef.value) {
      editorRef.value.scrollTop = 0
      editorRef.value.scrollLeft = 0
    }
  } catch (e) {
    ElMessage.error('读取文件失败')
  } finally {
    loading.value = false
  }
}

async function handleSave() {
  if (!activeFile.value) return

  // JSON 格式校验
  if (activeFile.value.endsWith('.json')) {
    try {
      JSON.parse(editContent.value)
    } catch (e) {
      ElMessage.error('JSON 格式错误，请检查后再保存')
      return
    }
  }

  try {
    await ElMessageBox.confirm(
      '确认保存？保存后可选择重启服务使配置立即生效。',
      '保存配置',
      { confirmButtonText: '保存', cancelButtonText: '取消', type: 'warning' }
    )
  } catch {
    return
  }

  saving.value = true
  try {
    await saveConfigFile(activeFile.value, editContent.value)
    originalContent.value = editContent.value
    ElMessage.success('保存成功')
    // 刷新文件列表（修改时间可能变了）
    await loadFileList()

    // 询问是否重启
    try {
      await ElMessageBox.confirm(
        '配置已保存。是否立即重启服务使配置生效？\n重启期间约 3-5 秒不可用。',
        '重启服务',
        { confirmButtonText: '立即重启', cancelButtonText: '稍后', type: 'warning' }
      )
      await restartService()
      ElMessage.success('服务正在重启...')
    } catch { /* 用户选择稍后 */ }
  } catch (e: any) {
    ElMessage.error(e?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

function handleReset() {
  if (!hasChanges.value) return
  editContent.value = originalContent.value
  ElMessage.success('已恢复到上次保存的内容')
}

/** 键盘快捷键：Ctrl+S 保存；Tab 缩进（同一元素不能绑定两个 @keydown，在此转发） */
function onEditorKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key === 's') {
    e.preventDefault()
    if (hasChanges.value) handleSave()
  }
  onEditorKeydownTab(e)
}

/** Tab 键插入缩进 */
function onEditorKeydownTab(e: KeyboardEvent) {
  if (e.key === 'Tab') {
    e.preventDefault()
    const el = e.target as HTMLTextAreaElement
    const start = el.selectionStart
    const end = el.selectionEnd
    const val = el.value
    editContent.value = val.substring(0, start) + '  ' + val.substring(end)
    nextTick(() => {
      el.selectionStart = el.selectionEnd = start + 2
    })
  }
}
</script>

<template>
  <div class="config-editor-page">
    <!-- 左侧文件列表 -->
    <div class="file-sidebar">
      <div class="sidebar-header">配置文件</div>
      <div class="sidebar-source" v-if="baseDirectory" :title="baseDirectory">
        <span class="source-icon">📁</span>
        <span class="source-path">{{ baseDirectory }}</span>
      </div>
      <div class="file-list">
        <div
          v-for="f in files"
          :key="f.name"
          class="file-item"
          :class="{ active: f.name === activeFile }"
          @click="selectFile(f.name)"
        >
          <span class="file-type-badge" :style="{ color: typeColors[f.type] || '#999', background: (typeColors[f.type] || '#999') + '18' }">
            {{ f.type.toUpperCase() }}
          </span>
          <div class="file-info">
            <div class="file-name">{{ f.name }}</div>
            <div class="file-meta">{{ f.modified }}</div>
          </div>
          <span class="file-size">{{ f.size }}</span>
        </div>
        <div v-if="files.length === 0" class="empty-tip">暂无可编辑的配置文件</div>
      </div>
    </div>

    <!-- 右侧编辑器 -->
    <div class="editor-area">
      <!-- 顶部工具栏 -->
      <div class="editor-toolbar">
        <div class="toolbar-left">
          <span class="current-file" v-if="activeFile">📄 {{ activeFile }}</span>
          <span class="separator" v-if="fileModified">|</span>
          <span class="modified-time" v-if="fileModified">最后修改: {{ fileModified }}</span>
        </div>
        <div class="toolbar-right">
          <el-button size="small" @click="handleReset" :disabled="!hasChanges">重置</el-button>
          <el-button size="small" type="primary" @click="handleSave" :loading="saving" :disabled="!hasChanges || !activeFile">
            💾 保存 Ctrl+S
          </el-button>
        </div>
      </div>

      <!-- 代码编辑区 -->
      <div class="editor-wrapper" v-if="activeFile">
        <div class="line-numbers">
          <div v-for="n in lineCount" :key="n" class="line-num">{{ n }}</div>
        </div>
        <textarea
          ref="editorRef"
          v-model="editContent"
          class="code-editor"
          spellcheck="false"
          :disabled="loading"
          @keydown="onEditorKeydown"
        ></textarea>
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
          <span class="file-format">{{ activeFile?.split('.').pop()?.toUpperCase() || '-' }} · UTF-8 · LF</span>
        </div>
        <div class="status-right">
          <span v-if="serviceStartTime">服务启动: {{ serviceStartTime }}</span>
        </div>
      </div>
    </div>
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
  background: #fff;
  display: flex;
  flex-direction: column;
}

.sidebar-header {
  padding: 14px 16px;
  font-size: 14px;
  font-weight: 600;
  color: #303133;
  border-bottom: 1px solid #f0f0f0;
}

.sidebar-source {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 12px;
  color: #909399;
  background: #fafafa;
  border-bottom: 1px solid #f0f0f0;
  overflow: hidden;
}

.sidebar-source .source-icon {
  flex-shrink: 0;
}

.sidebar-source .source-path {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.file-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  cursor: pointer;
  border-left: 3px solid transparent;
  transition: background 0.15s;
}

.file-item:hover {
  background: #f5f7fa;
}

.file-item.active {
  background: #ecf5ff;
  border-left-color: #3b82f6;
}

.file-type-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 4px;
  font-size: 10px;
  font-weight: 700;
  font-family: monospace;
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
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-item.active .file-name {
  color: #3b82f6;
  font-weight: 600;
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

/* ===== 右侧编辑器 ===== */
.editor-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 20px;
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.current-file {
  font-size: 13px;
  font-weight: 500;
  color: #606266;
}

.separator {
  color: #dcdfe6;
}

.modified-time {
  font-size: 12px;
  color: #909399;
}

.toolbar-right {
  display: flex;
  gap: 8px;
}

/* ===== 代码编辑区 ===== */
.editor-wrapper {
  flex: 1;
  display: flex;
  background: #1e1e1e;
  overflow: hidden;
  position: relative;
}

.line-numbers {
  width: 50px;
  padding: 16px 8px 16px 0;
  text-align: right;
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  font-size: 13px;
  line-height: 1.6;
  color: #5a5a5a;
  user-select: none;
  overflow: hidden;
  flex-shrink: 0;
  background: #1e1e1e;
}

.line-num {
  height: calc(13px * 1.6);
}

.code-editor {
  flex: 1;
  padding: 16px 16px 16px 12px;
  background: #1e1e1e;
  color: #d4d4d4;
  font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
  font-size: 13px;
  line-height: 1.6;
  border: none;
  outline: none;
  resize: none;
  tab-size: 2;
  white-space: pre;
  overflow: auto;
}

.code-editor:disabled {
  opacity: 0.6;
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
  padding: 6px 20px;
  background: #fff;
  border-top: 1px solid #e4e7ed;
  font-size: 12px;
  flex-shrink: 0;
}

.status-left, .status-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.save-indicator {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.save-indicator.saved {
  color: #67c23a;
}

.save-indicator.modified {
  color: #e6a23c;
}

.file-format {
  color: #909399;
}

.status-right {
  color: #909399;
}
</style>
