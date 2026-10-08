<template>
  <div class="api-debug-view">
    <!-- 左右分栏容器 -->
    <div class="split-container">
      <!-- 左侧：请求区 -->
      <div class="left-panel" :style="{ width: leftPanelWidth + 'px' }">
        <!-- 顶部请求行 -->
        <div class="request-bar">
          <el-select v-model="method" class="req-method" size="large">
            <el-option v-for="m in METHODS" :key="m" :label="m" :value="m" />
          </el-select>
          <el-input
            v-model="url"
            class="req-url"
            placeholder="http://localhost:8030/api/..."
            spellcheck="false"
            size="large"
            @keyup.enter="send"
          />
          <el-button type="primary" size="large" :loading="sending" @click="send">
            发送
          </el-button>
          <el-dropdown class="save-dropdown">
            <el-button size="large">
              保存
              <el-icon class="el-icon--right"><arrow-down /></el-icon>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item @click="saveToHistory">保存到历史</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>

        <!-- Tab 页签 -->
        <el-tabs v-model="activeTab" class="debug-tabs">
      <!-- Params Tab -->
      <el-tab-pane label="Params" name="params">
        <div class="params-section">
          <!-- Path 参数 -->
          <div v-if="editablePathParams.length > 0" class="param-group">
            <div class="block-head">
              <span>Path 参数</span>
              <el-button link type="primary" size="small" @click="addPathParam">+ 添加</el-button>
            </div>
            <div v-for="(row, i) in editablePathParams" :key="i" class="param-row">
              <el-input v-model="row.name" placeholder="参数名" size="small" spellcheck="false" class="param-name-input" />
              <el-input v-model="row.value" placeholder="值" size="small" spellcheck="false" class="param-value-input" />
              <el-button link type="danger" size="small" @click="removePathParam(i)">删除</el-button>
            </div>
          </div>
          <div v-else class="param-group">
            <div class="block-head">
              <span>Path 参数</span>
              <el-button link type="primary" size="small" @click="addPathParam">+ 添加</el-button>
            </div>
            <div class="empty-tip">暂无 Path 参数</div>
          </div>
          
          <!-- Query 参数 -->
          <div class="param-group">
            <div class="block-head">
              <span>Query 参数</span>
              <el-button link type="primary" size="small" @click="addQueryParam">+ 添加</el-button>
            </div>
            <div v-for="(row, i) in editableQueryParams" :key="i" class="param-row">
              <el-input v-model="row.name" placeholder="参数名" size="small" spellcheck="false" class="param-name-input" />
              <el-input v-model="row.value" placeholder="值" size="small" spellcheck="false" class="param-value-input" />
              <el-button link type="danger" size="small" @click="removeQueryParam(i)">删除</el-button>
            </div>
          </div>
        </div>
      </el-tab-pane>

      <!-- Body Tab -->
      <el-tab-pane label="Body" name="body">
        <div class="body-section">
          <!-- Body 类型选择 -->
          <div class="body-type-bar">
            <div class="body-type-options">
              <span
                v-for="t in BODY_TYPES"
                :key="t"
                class="body-type-item"
                :class="{ active: bodyType === t }"
                @click="bodyType = t"
              >
                {{ t }}
              </span>
            </div>
            <div v-if="bodyType === 'JSON'" class="body-actions">
              <el-button link type="primary" size="small" @click="autoGenerate">
                <el-icon><magic-stick /></el-icon> 自动生成
              </el-button>
              <el-button link type="primary" size="small" @click="formatBody">
                <el-icon><document-checked /></el-icon> 格式化
              </el-button>
            </div>
            <div v-if="bodyType === 'JSON'" class="content-type">application/json</div>
          </div>

          <!-- 编辑器区域 -->
          <div v-if="bodyType === 'JSON'" class="editor-wrapper">
            <div ref="editorContainer" class="code-editor"></div>
          </div>
          <div v-else-if="bodyType === 'none'" class="empty-tip">无请求体</div>
          <div v-else-if="bodyType === 'form-data'" class="form-data-section">
            <div class="block-head">
              <span>Form Data</span>
              <div class="block-actions">
                <el-button link type="primary" size="small" @click="toggleFormDataBulkEdit">
                  {{ formDataBulkMode ? '表单模式' : '批量编辑' }}
                </el-button>
                <el-button link type="primary" size="small" @click="addFormDataRow">+ 添加</el-button>
              </div>
            </div>
            <div v-if="!formDataBulkMode">
              <div v-for="(row, i) in formDataRows" :key="i" class="form-row">
                <el-checkbox v-model="row.enabled" />
                <el-input v-model="row.key" placeholder="Key" size="small" spellcheck="false" />
                <template v-if="row.type === 'file'">
                  <el-button size="small" @click="selectFile(i)">
                    {{ row.file ? row.file.name : '选择文件' }}
                  </el-button>
                  <el-button v-if="row.file" link type="danger" size="small" @click="removeFile(i)">移除</el-button>
                </template>
                <template v-else>
                  <el-input v-model="row.value" placeholder="Value" size="small" spellcheck="false" />
                </template>
                <el-dropdown size="small" @command="(cmd: string) => handleFormDataRowCommand(i, cmd)">
                  <el-button link size="small">
                    {{ row.type === 'file' ? '文件' : '文本' }}
                    <el-icon class="el-icon--right"><arrow-down /></el-icon>
                  </el-button>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item command="text">文本</el-dropdown-item>
                      <el-dropdown-item command="file">文件</el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
                <el-input v-model="row.description" placeholder="描述" size="small" spellcheck="false" class="desc-input" />
                <el-button link type="danger" size="small" @click="removeFormDataRow(i)">删除</el-button>
              </div>
            </div>
            <div v-else class="bulk-edit-area">
              <el-input
                v-model="formDataBulkText"
                type="textarea"
                :rows="10"
                placeholder="每行一个 key=value，例如：&#10;name=张三&#10;age=18"
                spellcheck="false"
              />
              <div class="bulk-tip">提示：每行格式为 key=value，空行会被忽略（批量编辑仅支持文本类型）</div>
            </div>
            <!-- 隐藏的文件输入 -->
            <input ref="fileInput" type="file" style="display: none" @change="handleFileSelect" />
          </div>
          <div v-else-if="bodyType === 'x-www-form-urlencoded'" class="form-data-section">
            <div class="block-head">
              <span>x-www-form-urlencoded</span>
              <div class="block-actions">
                <el-button link type="primary" size="small" @click="toggleFormUrlencodedBulkEdit">
                  {{ formUrlencodedBulkMode ? '表单模式' : '批量编辑' }}
                </el-button>
                <el-button link type="primary" size="small" @click="addFormUrlencodedRow">+ 添加</el-button>
              </div>
            </div>
            <div v-if="!formUrlencodedBulkMode">
              <div v-for="(row, i) in formUrlencodedRows" :key="i" class="form-row">
                <el-checkbox v-model="row.enabled" />
                <el-input v-model="row.key" placeholder="Key" size="small" spellcheck="false" />
                <el-input v-model="row.value" placeholder="Value" size="small" spellcheck="false" />
                <el-input v-model="row.description" placeholder="描述" size="small" spellcheck="false" class="desc-input" />
                <el-button link type="danger" size="small" @click="removeFormUrlencodedRow(i)">删除</el-button>
              </div>
            </div>
            <div v-else class="bulk-edit-area">
              <el-input
                v-model="formUrlencodedBulkText"
                type="textarea"
                :rows="10"
                placeholder="每行一个 key=value，例如：&#10;name=张三&#10;age=18"
                spellcheck="false"
              />
              <div class="bulk-tip">提示：每行格式为 key=value，空行会被忽略</div>
            </div>
          </div>
          <div v-else class="empty-tip">{{ bodyType }} 模式暂未实现</div>
        </div>
      </el-tab-pane>

      <!-- Headers Tab -->
      <el-tab-pane label="Headers" name="headers">
        <div class="headers-section">
          <div class="block-head">
            <span>请求头</span>
            <el-button link type="primary" size="small" @click="addHeaderRow">+ 添加</el-button>
          </div>
          <div v-for="(row, i) in headerRows" :key="i" class="header-row">
            <el-input v-model="row.key" placeholder="Header 名" size="small" spellcheck="false" />
            <el-input v-model="row.value" placeholder="值" size="small" spellcheck="false" />
            <el-button link type="danger" size="small" @click="removeHeaderRow(i)">删除</el-button>
          </div>
        </div>
      </el-tab-pane>

      <!-- 占位 Tab -->
      <el-tab-pane label="Cookies" name="cookies" disabled />
      <el-tab-pane label="Auth" name="auth" disabled />
      <el-tab-pane label="前置操作" name="pre" disabled />
      <el-tab-pane label="后置操作" name="post" disabled />
      <el-tab-pane label="设置" name="settings" disabled />
    </el-tabs>
      </div>

      <!-- 中间分隔条（可拖拽） -->
      <div
        class="splitter"
        @mousedown="startDrag"
        @dblclick="resetSplit"
      >
        <div class="splitter-handle"></div>
      </div>

      <!-- 右侧：响应区 -->
      <div class="right-panel">
        <div v-if="response" class="response-section">
          <div class="response-header">
            <span class="response-title">返回响应</span>
            <div class="response-meta">
              <el-tag :type="statusTagType" size="small" class="status-code">{{ statusTagText }}</el-tag>
              <span class="response-time">{{ response.elapsedMs }} ms</span>
              <span class="response-size">{{ response.body.length }} 字符</span>
              <el-button link type="primary" size="small" @click="copyResponse">复制</el-button>
            </div>
          </div>
          <el-alert v-if="response.error" :title="response.error" type="error" :closable="false" class="response-error" />
          <div ref="responseEditorContainer" class="response-editor"></div>
        </div>
        <div v-else class="response-empty">
          <el-icon class="rocket-icon"><promotion /></el-icon>
          <div>点击"发送"按钮获取返回结果</div>
        </div>

        <!-- 历史记录 -->
        <div v-if="history.length > 0" class="history-section">
          <div class="block-head">
            <span>历史记录</span>
            <el-button link type="danger" size="small" @click="clearHistory">清空</el-button>
          </div>
          <div class="history-list">
            <div
              v-for="(item, i) in history"
              :key="i"
              class="history-item"
              @click="loadHistoryItem(item)"
            >
              <el-tag :type="methodTagType(item.method)" size="small" class="history-method">{{ item.method }}</el-tag>
              <span class="history-url" :title="item.url">{{ item.url }}</span>
              <span class="history-time">{{ formatHistoryTime(item.time) }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, onMounted, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { ArrowDown, MagicStick, DocumentChecked, Promotion } from '@element-plus/icons-vue'
import { EditorView, basicSetup } from 'codemirror'
import { EditorState } from '@codemirror/state'
import { json } from '@codemirror/lang-json'
import { oneDark } from '@codemirror/theme-one-dark'
import { getAuthToken } from '@/api/request'
import { sendApiDebug, type ApiDebugResponse, type ApiDebugFile } from '@/common/api/apiSpec'
import type { ApiSpecParamDto, ApiSpecTypeDto } from '@/common/api/apiSpec'

const props = defineProps<{
  /** 嵌入模式：预填接口信息（从 API 文档打开时传入） */
  init?: {
    method: string
    url: string
    headers?: Record<string, string>
    body?: string
    params?: ApiSpecParamDto[]
    types?: Record<string, ApiSpecTypeDto>
  } | null
}>()

const METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS']
const BODY_TYPES = ['none', 'form-data', 'x-www-form-urlencoded', 'JSON', 'XML', 'Text', 'Binary', 'GraphQL']
const HISTORY_KEY = 'api-debug-history-v2'
const HISTORY_MAX = 50

interface DebugHistoryItem {
  time: number
  method: string
  url: string
  headers: Record<string, string>
  body: string
  bodyType: string
}

const method = ref('GET')
const url = ref('')
const body = ref('')
const bodyType = ref('JSON')
const timeoutSec = ref(30)
const attachToken = ref(true)
const headerRows = ref([{ key: '', value: '' }])

// 左右分栏拖拽相关
const leftPanelWidth = ref(850)  // 左侧面板默认宽度
const isDragging = ref(false)
const startX = ref(0)
const startWidth = ref(0)

interface FormRow {
  key: string
  value: string
  enabled: boolean
  description?: string
  type?: 'text' | 'file'
  file?: File
}
const formDataRows = ref<FormRow[]>([{ key: '', value: '', enabled: true, type: 'text' }])
const formUrlencodedRows = ref<FormRow[]>([{ key: '', value: '', enabled: true, type: 'text' }])

// 批量编辑模式
const formDataBulkMode = ref(false)
const formDataBulkText = ref('')
const formUrlencodedBulkMode = ref(false)
const formUrlencodedBulkText = ref('')

// 文件选择相关
const fileInput = ref<HTMLInputElement>()
const currentFileRowIndex = ref(-1)
const sending = ref(false)
const response = ref<ApiDebugResponse | null>(null)
const history = ref<DebugHistoryItem[]>(loadHistory())
const activeTab = ref('params')

// 参数列表（从接口解析）
const pathParams = ref<ApiSpecParamDto[]>([])
const queryParams = ref<ApiSpecParamDto[]>([])
const headerParams = ref<ApiSpecParamDto[]>([])
const bodyParams = ref<ApiSpecParamDto[]>([])
const typeDefs = ref<Record<string, ApiSpecTypeDto>>({})

// 可编辑的参数行（用于 Params Tab）
interface EditableParam {
  name: string
  value: string
  typeText?: string
  description?: string
}
const editablePathParams = ref<EditableParam[]>([])
const editableQueryParams = ref<EditableParam[]>([])

// CodeMirror 编辑器实例
let editor: EditorView | null = null
let responseEditor: EditorView | null = null
const editorContainer = ref<HTMLElement>()
const responseEditorContainer = ref<HTMLElement>()

// 打开时预填
watch(() => props.init, (init) => {
  if (!init) return
  method.value = (init.method || 'GET').toUpperCase()
  url.value = init.url
  body.value = init.body || ''
  bodyType.value = init.body ? 'JSON' : 'none'
  
  // 预填请求头
  if (init.headers && Object.keys(init.headers).length > 0) {
    headerRows.value = Object.entries(init.headers).map(([key, value]) => ({ key, value }))
  } else {
    headerRows.value = [{ key: '', value: '' }]
  }
  
  // 预填参数
  if (init.params) {
    pathParams.value = init.params.filter(p => p.in === 'path').map(p => ({ ...p, defaultValue: getDefaultValue(p.typeText) }))
    queryParams.value = init.params.filter(p => p.in === 'query').map(p => ({ ...p, defaultValue: getDefaultValue(p.typeText) }))
    headerParams.value = init.params.filter(p => p.in === 'header')
    bodyParams.value = init.params.filter(p => p.in === 'body')
    
    // 转换为可编辑的参数行
    editablePathParams.value = pathParams.value.map(p => ({
      name: p.name,
      value: p.defaultValue || '',
      typeText: p.typeText,
      description: p.description,
    }))
    editableQueryParams.value = queryParams.value.map(p => ({
      name: p.name,
      value: p.defaultValue || '',
      typeText: p.typeText,
      description: p.description,
    }))
  } else {
    editablePathParams.value = []
    editableQueryParams.value = []
  }
  
  // 类型定义
  if (init.types) {
    typeDefs.value = init.types
  }
  
  // 自动生成请求体
  if (bodyParams.value.length > 0 && bodyType.value === 'JSON') {
    autoGenerate()
  }
  
  // 更新编辑器内容
  nextTick(() => updateEditor())
}, { immediate: true })

// 状态码标签
const statusTagType = computed(() => {
  const c = response.value?.statusCode ?? 0
  if (c === 0) return 'danger'
  if (c < 300) return 'success'
  if (c < 400) return 'warning'
  return 'danger'
})
const statusTagText = computed(() => {
  const r = response.value
  if (!r) return ''
  if (r.statusCode === 0) return '网络错误'
  return `${r.statusCode} ${r.statusText}`.trim()
})

// 初始化编辑器
onMounted(() => {
  if (editorContainer.value) {
    editor = new EditorView({
      doc: body.value,
      extensions: [
        basicSetup,
        json(),
        oneDark,
        EditorView.updateListener.of((update) => {
          if (update.docChanged) {
            body.value = update.state.doc.toString()
          }
        }),
      ],
      parent: editorContainer.value,
    })
  }
})

// 更新编辑器内容
function updateEditor() {
  if (editor) {
    editor.dispatch({
      changes: { from: 0, to: editor.state.doc.length, insert: body.value },
    })
  }
}

// 更新响应编辑器
function updateResponseEditor() {
  if (!responseEditorContainer.value) return
  if (responseEditor) {
    responseEditor.destroy()
  }
  const respBody = response.value?.body || ''
  let formatted = respBody
  try {
    formatted = JSON.stringify(JSON.parse(respBody), null, 2)
  } catch {
    // 非 JSON 保持原样
  }
  responseEditor = new EditorView({
    doc: formatted,
    extensions: [
      basicSetup,
      json(),
      oneDark,
      EditorState.readOnly.of(true),
    ],
    parent: responseEditorContainer.value,
  })
}

watch(response, () => {
  nextTick(() => updateResponseEditor())
})

// 自动生成请求体
function autoGenerate() {
  if (bodyParams.value.length === 0) {
    ElMessage.warning('该接口无请求体参数')
    return
  }
  
  let obj: any = {}
  if (bodyParams.value.length === 1) {
    obj = buildDefaultValue(bodyParams.value[0].typeText, typeDefs.value)
  } else {
    for (const p of bodyParams.value) {
      obj[p.name] = buildDefaultValue(p.typeText, typeDefs.value)
    }
  }
  
  body.value = JSON.stringify(obj, null, 2)
  updateEditor()
  ElMessage.success('已自动生成请求体')
}

// 格式化 JSON
function formatBody() {
  try {
    const obj = JSON.parse(body.value)
    body.value = JSON.stringify(obj, null, 2)
    updateEditor()
    ElMessage.success('格式化成功')
  } catch {
    ElMessage.warning('JSON 格式错误，无法格式化')
  }
}

// 构建默认值
function getDefaultValue(typeText: string): string {
  return String(buildDefaultValue(typeText, typeDefs.value))
}

// 文件转 Base64
function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      // 去掉 data:xxx;base64, 前缀
      const base64 = result.split(',')[1] || ''
      resolve(base64)
    }
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

function buildDefaultValue(typeText: string, types: Record<string, ApiSpecTypeDto>): any {
  const t = (typeText || '').replace(/\s/g, '')
  if (t.endsWith('?')) return buildDefaultValue(t.slice(0, -1), types)
  if (t === 'string') return ''
  if (['int', 'long', 'short', 'byte'].includes(t)) return 0
  if (['double', 'float', 'decimal'].includes(t)) return 0.0
  if (t === 'bool') return false
  if (t === 'datetime' || t === 'datetimeoffset') return '2024-01-01T00:00:00'
  if (t.startsWith('list<') || t.startsWith('ienumerable<') || t.endsWith('[]')) return []
  if (t.startsWith('dictionary<')) return {}
  
  const typeName = t.split('<')[0]
  const typeDef = types[typeName]
  if (typeDef) {
    if (typeDef.isEnum) return typeDef.enumValues[0] || ''
    const obj: any = {}
    for (const f of typeDef.fields) {
      obj[f.name] = buildDefaultValue(f.typeText, types)
    }
    return obj
  }
  return null
}

// 发送请求
async function send() {
  const target = url.value.trim()
  if (!target) return ElMessage.warning('请填写目标接口地址')
  
  // 构建请求头
  const headers: Record<string, string> = {}
  for (const r of headerRows.value) {
    const k = r.key.trim()
    if (k) headers[k] = r.value
  }
  
  // 自动注入 Token
  const token = getAuthToken()
  if (attachToken.value && token && !Object.keys(headers).some(k => k.toLowerCase() === 'authorization')) {
    headers.Authorization = `Bearer ${token}`
  }
  
  // 处理 URL 参数（替换 path 参数 + 拼接 query 参数）
  let finalUrl = target
  for (const p of editablePathParams.value) {
    const name = p.name.trim()
    if (name) {
      finalUrl = finalUrl.replace(`{${name}}`, encodeURIComponent(p.value || ''))
    }
  }
  const queryParts: string[] = []
  for (const p of editableQueryParams.value) {
    const name = p.name.trim()
    if (name && p.value) {
      queryParts.push(`${encodeURIComponent(name)}=${encodeURIComponent(p.value)}`)
    }
  }
  if (queryParts.length > 0) {
    finalUrl += (finalUrl.includes('?') ? '&' : '?') + queryParts.join('&')
  }
  
  sending.value = true
  try {
    // 根据 bodyType 构建请求体
    let requestBody: string | undefined
    if (bodyType.value === 'JSON') {
      requestBody = body.value || undefined
    } else if (bodyType.value === 'x-www-form-urlencoded') {
      const formData: Record<string, string> = {}
      for (const r of formUrlencodedRows.value) {
        const k = r.key.trim()
        if (k && r.enabled) {
          formData[k] = r.value
        }
      }
      
      response.value = await sendApiDebug({
        method: method.value,
        url: finalUrl,
        headers,
        formData,
        timeoutMs: (timeoutSec.value || 30) * 1000,
      })
      saveHistory()
      return  // 提前返回
    } else if (bodyType.value === 'form-data') {
      // multipart/form-data（支持文件上传）
      const formData: Record<string, string> = {}
      const files: ApiDebugFile[] = []
      
      for (const r of formDataRows.value) {
        const k = r.key.trim()
        if (!k || !r.enabled) continue
        
        if (r.type === 'file' && r.file) {
          // 文件类型：读取为 Base64
          const base64 = await fileToBase64(r.file)
          files.push({
            fieldName: k,
            fileName: r.file.name,
            content: base64,
            contentType: r.file.type || 'application/octet-stream',
          })
        } else if (r.type !== 'file') {
          // 文本类型
          formData[k] = r.value
        }
      }
      
      // 调用后端接口
      response.value = await sendApiDebug({
        method: method.value,
        url: finalUrl,
        headers,
        formData,
        files: files.length > 0 ? files : undefined,
        timeoutMs: (timeoutSec.value || 30) * 1000,
      })
      saveHistory()
      return  // 提前返回，避免下面的重复调用
    }
    
    response.value = await sendApiDebug({
      method: method.value,
      url: finalUrl,
      headers,
      body: requestBody,
      timeoutMs: (timeoutSec.value || 30) * 1000,
    })
    saveHistory()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    sending.value = false
  }
}

// 历史记录
function loadHistory(): DebugHistoryItem[] {
  try {
    const raw = localStorage.getItem(HISTORY_KEY)
    if (!raw) return []
    const list = JSON.parse(raw)
    return Array.isArray(list) ? list : []
  } catch {
    return []
  }
}

function saveHistory() {
  const item: DebugHistoryItem = {
    time: Date.now(),
    method: method.value,
    url: url.value.trim(),
    headers: Object.fromEntries(headerRows.value.filter(r => r.key.trim()).map(r => [r.key.trim(), r.value])),
    body: body.value,
    bodyType: bodyType.value,
  }
  history.value = history.value.filter(h => !(h.method === item.method && h.url === item.url))
  history.value.unshift(item)
  history.value = history.value.slice(0, HISTORY_MAX)
  try {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history.value))
  } catch {
    /* 存储超限等异常忽略 */
  }
}

function saveToHistory() {
  saveHistory()
  ElMessage.success('已保存到历史')
}

async function copyResponse() {
  const text = response.value?.body || ''
  if (!text) return ElMessage.warning('响应体为空')
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败：浏览器未授权剪贴板')
  }
}

function addHeaderRow() {
  headerRows.value.push({ key: '', value: '' })
}
function removeHeaderRow(i: number) {
  headerRows.value.splice(i, 1)
  if (headerRows.value.length === 0) headerRows.value.push({ key: '', value: '' })
}

function addFormDataRow() {
  formDataRows.value.push({ key: '', value: '', enabled: true, type: 'text' })
}
function removeFormDataRow(i: number) {
  formDataRows.value.splice(i, 1)
  if (formDataRows.value.length === 0) formDataRows.value.push({ key: '', value: '', enabled: true, type: 'text' })
}
function toggleFormDataBulkEdit() {
  if (formDataBulkMode.value) {
    // 从批量编辑模式切换回表单模式：解析文本
    const lines = formDataBulkText.value.split('\n').filter(l => l.trim())
    const rows: FormRow[] = []
    for (const line of lines) {
      const eqIdx = line.indexOf('=')
      if (eqIdx > 0) {
        const key = line.substring(0, eqIdx).trim()
        const value = line.substring(eqIdx + 1).trim()
        rows.push({ key, value, enabled: true, type: 'text' })
      }
    }
    formDataRows.value = rows.length > 0 ? rows : [{ key: '', value: '', enabled: true, type: 'text' }]
    formDataBulkMode.value = false
  } else {
    // 从表单模式切换到批量编辑模式：生成文本（仅文本类型）
    const lines = formDataRows.value
      .filter(r => r.key.trim() && r.type !== 'file')
      .map(r => `${r.key}=${r.value}`)
    formDataBulkText.value = lines.join('\n')
    formDataBulkMode.value = true
  }
}

// 文件选择相关方法
function selectFile(rowIndex: number) {
  currentFileRowIndex.value = rowIndex
  if (fileInput.value) {
    fileInput.value.click()
  }
}

function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files || input.files.length === 0 || currentFileRowIndex.value < 0) return
  
  const file = input.files[0]
  formDataRows.value[currentFileRowIndex.value].file = file
  formDataRows.value[currentFileRowIndex.value].value = file.name
  
  // 重置 input
  input.value = ''
}

function removeFile(rowIndex: number) {
  formDataRows.value[rowIndex].file = undefined
  formDataRows.value[rowIndex].value = ''
}

function handleFormDataRowCommand(rowIndex: number, command: string) {
  formDataRows.value[rowIndex].type = command as 'text' | 'file'
  if (command === 'file') {
    formDataRows.value[rowIndex].value = ''
  }
}

function addFormUrlencodedRow() {
  formUrlencodedRows.value.push({ key: '', value: '', enabled: true, type: 'text' })
}
function removeFormUrlencodedRow(i: number) {
  formUrlencodedRows.value.splice(i, 1)
  if (formUrlencodedRows.value.length === 0) formUrlencodedRows.value.push({ key: '', value: '', enabled: true, type: 'text' })
}
function toggleFormUrlencodedBulkEdit() {
  if (formUrlencodedBulkMode.value) {
    // 从批量编辑模式切换回表单模式：解析文本
    const lines = formUrlencodedBulkText.value.split('\n').filter(l => l.trim())
    const rows: FormRow[] = []
    for (const line of lines) {
      const eqIdx = line.indexOf('=')
      if (eqIdx > 0) {
        const key = line.substring(0, eqIdx).trim()
        const value = line.substring(eqIdx + 1).trim()
        rows.push({ key, value, enabled: true, type: 'text' })
      }
    }
    formUrlencodedRows.value = rows.length > 0 ? rows : [{ key: '', value: '', enabled: true, type: 'text' }]
    formUrlencodedBulkMode.value = false
  } else {
    // 从表单模式切换到批量编辑模式：生成文本
    const lines = formUrlencodedRows.value
      .filter(r => r.key.trim())
      .map(r => `${r.key}=${r.value}`)
    formUrlencodedBulkText.value = lines.join('\n')
    formUrlencodedBulkMode.value = true
  }
}

function addPathParam() {
  editablePathParams.value.push({ name: '', value: '' })
}
function removePathParam(i: number) {
  editablePathParams.value.splice(i, 1)
}

function addQueryParam() {
  editableQueryParams.value.push({ name: '', value: '' })
}
function removeQueryParam(i: number) {
  editableQueryParams.value.splice(i, 1)
}

// 左右分栏拖拽方法
function startDrag(e: MouseEvent) {
  isDragging.value = true
  startX.value = e.clientX
  startWidth.value = leftPanelWidth.value
  
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

function onDrag(e: MouseEvent) {
  if (!isDragging.value) return
  const delta = e.clientX - startX.value
  const newWidth = startWidth.value + delta
  // 限制最小和最大宽度
  leftPanelWidth.value = Math.max(400, Math.min(newWidth, window.innerWidth - 400))
}

function stopDrag() {
  isDragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

function resetSplit() {
  leftPanelWidth.value = 850
}

// 历史记录辅助方法
function methodTagType(method: string) {
  const m = method.toUpperCase()
  if (m === 'GET') return 'success'
  if (m === 'POST') return 'primary'
  if (m === 'PUT') return 'warning'
  if (m === 'DELETE') return 'danger'
  return 'info'
}

function formatHistoryTime(timestamp: number) {
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  
  if (diffMins < 1) return '刚刚'
  if (diffMins < 60) return `${diffMins}分钟前`
  
  const diffHours = Math.floor(diffMins / 60)
  if (diffHours < 24) return `${diffHours}小时前`
  
  const diffDays = Math.floor(diffHours / 24)
  if (diffDays < 7) return `${diffDays}天前`
  
  return date.toLocaleDateString()
}

function loadHistoryItem(item: DebugHistoryItem) {
  method.value = item.method
  url.value = item.url
  body.value = item.body
  bodyType.value = item.bodyType
  if (item.headers && Object.keys(item.headers).length > 0) {
    headerRows.value = Object.entries(item.headers).map(([key, value]) => ({ key, value }))
  }
}

function clearHistory() {
  history.value = []
  localStorage.removeItem(HISTORY_KEY)
}
</script>

<style scoped>
.api-debug-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 16px;
  gap: 0;
}

/* 左右分栏容器 */
.split-container {
  display: flex;
  flex: 1;
  overflow: hidden;
  gap: 0;
}

/* 左侧面板 */
.left-panel {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 400px;
}

/* 中间分隔条 */
.splitter {
  width: 6px;
  background: var(--el-border-color-lighter);
  cursor: col-resize;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.2s;
  flex-shrink: 0;
}
.splitter:hover {
  background: var(--el-color-primary-light-5);
}
.splitter-handle {
  width: 2px;
  height: 40px;
  background: var(--el-border-color);
  border-radius: 2px;
}
.splitter:hover .splitter-handle {
  background: var(--el-color-primary);
}

/* 右侧面板 */
.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 400px;
}

/* 请求行 */
.request-bar {
  display: flex;
  gap: 12px;
  align-items: center;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  flex-shrink: 0;
}
.req-method { width: 120px; flex: none; }
.req-url { flex: 1; }
.save-dropdown { margin-left: 8px; }

/* Tab 页签 */
.debug-tabs {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.debug-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow: auto;
  padding: 16px 0;
}

/* Params */
.params-section {
  padding: 0 16px;
}
.param-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.param-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
}
.param-name {
  width: 150px;
  font-weight: 600;
  font-family: Consolas, Monaco, monospace;
}
.param-type {
  width: 100px;
  color: var(--el-color-primary);
  font-size: 12px;
}
.param-value {
  flex: 1;
}
.param-desc {
  width: 200px;
  color: var(--text-sub, #909399);
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Body */
.body-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 0 16px;
}
.body-type-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.body-type-options {
  display: flex;
  gap: 6px;
  flex-wrap: nowrap;
}
.body-type-item {
  padding: 4px 10px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  color: var(--text-sub, #606266);
  white-space: nowrap;
  flex-shrink: 0;
}
.body-type-item:hover {
  background: var(--el-fill-color-light);
}
.body-type-item.active {
  background: var(--el-color-primary);
  color: #fff;
}
.body-actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
  flex-shrink: 0;
}
.content-type {
  color: var(--text-sub, #909399);
  font-size: 12px;
  font-family: Consolas, Monaco, monospace;
  flex-shrink: 0;
  white-space: nowrap;
}
.editor-wrapper {
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  overflow: hidden;
  flex: 1;
  min-height: 300px;
}
.code-editor {
  height: 100%;
}

/* Headers */
.headers-section {
  padding: 0 16px;
}
.block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
  font-weight: 600;
}
.header-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}
.header-row .el-input:first-child { width: 200px; flex: none; }
.header-row .el-input:nth-child(2) { flex: 1; }

/* Form Data */
.form-data-section {
  padding: 0 16px;
}
.form-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}
.form-row .el-checkbox {
  flex: none;
}
.form-row .el-input:first-of-type { width: 180px; flex: none; }
.form-row .el-input:nth-of-type(2) { flex: 1; }
.form-row .desc-input { width: 150px; flex: none; }
.block-actions {
  display: flex;
  gap: 8px;
}
.bulk-edit-area {
  margin-top: 8px;
}
.bulk-tip {
  margin-top: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* Params */
.params-section {
  padding: 0 16px;
}
.param-group {
  margin-bottom: 16px;
}
.param-group:last-child {
  margin-bottom: 0;
}
.param-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}
.param-name-input {
  width: 200px;
  flex: none;
}
.param-value-input {
  flex: 1;
}

/* 响应区 */
.response-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  overflow: hidden;
  margin-bottom: 12px;
}
.response-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: var(--el-fill-color-light);
  border-bottom: 1px solid var(--el-border-color-lighter);
  flex-shrink: 0;
}
.response-title {
  font-weight: 600;
  font-size: 14px;
}
.response-meta {
  display: flex;
  align-items: center;
  gap: 12px;
}
.status-code {
  font-weight: 600;
}
.response-time,
.response-size {
  font-size: 12px;
  color: var(--text-sub, #909399);
}
.response-error {
  margin: 12px 16px 0;
  flex-shrink: 0;
}
.response-editor {
  flex: 1;
  min-height: 200px;
}

/* 空状态 */
.response-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 0;
  color: var(--text-sub, #909399);
  gap: 16px;
}
.rocket-icon {
  font-size: 48px;
  opacity: 0.3;
}
.empty-tip {
  text-align: center;
  padding: 40px 0;
  color: var(--text-sub, #909399);
}

/* 历史记录 */
.history-section {
  flex-shrink: 0;
  max-height: 200px;
  overflow: auto;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  padding: 12px;
}
.history-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.history-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.2s;
}
.history-item:hover {
  background: var(--el-fill-color-light);
}
.history-method {
  flex: none;
  width: 60px;
  text-align: center;
}
.history-url {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  font-family: Consolas, Monaco, monospace;
}
.history-time {
  flex: none;
  font-size: 12px;
  color: var(--text-sub, #909399);
}

/* 暗色主题 */
html.dark .code-editor,
html.dark .response-editor {
  background: #0f172a;
}
</style>
