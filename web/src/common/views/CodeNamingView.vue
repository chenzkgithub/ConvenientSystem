<script setup lang="ts">
/**
 * 代码命名转换工具（增强版）
 * - 双模式：中文翻译（调 API，自动翻译） / 直接输入（中英文均可，实时拆词）
 * - 批量模式：多行输入，每行独立生成全部命名规则
 * - 10 种命名规则：camelCase / PascalCase / snake_case / kebab-case / UPPER_SNAKE / dot.case / Title Case / flatcase / SCREAMING-KEBAB / path/case
 * - 名称过长（拼合 > 20 字符）时额外生成缩短版：省略虚词 + 常见开发词汇缩写表
 * - 拼音兼底、示例词、历史记录、一键复制全部、点击值复制
 */
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { CopyDocument, EditPen, DocumentCopy, Loading, ArrowRight } from '@element-plus/icons-vue'
import { pinyin } from 'pinyin-pro'
import { httpGet } from '@/api/request'

/** 翻译 API 返回结构 */
interface TranslateResult {
  original: string
  translated: string
  words: string[]
}

/** 单条命名规则结果 */
interface NamingItem {
  rule: string
  value: string
}

/** 批量行结果 */
interface BatchRow {
  source: string
  words: string[]
  results: NamingItem[]
  /** 缩短版（该行命名过长时提供） */
  shortened?: {
    words: string[]
    results: NamingItem[]
  }
}

/** 工作模式：translate=中文翻译 / direct=直接输入 */
const mode = ref<'translate' | 'direct'>('translate')

/** 单行 / 批量 */
const batchMode = ref(false)

const inputText = ref('')
const batchText = ref('')
const loading = ref(false)
const result = ref<TranslateResult | null>(null)
const editedTranslation = ref('')

/** 快捷示例词 */
const EXAMPLES = ['用户管理', '订单查询', '数据导出', '权限控制', '日志记录', '配置中心']

/** 历史记录（localStorage 持久化） */
const HISTORY_KEY = 'code_naming_history'
const history = ref<string[]>(loadHistory())
function loadHistory(): string[] {
  try { return JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]') } catch { return [] }
}
function pushHistory(text: string) {
  const trimmed = text.trim()
  if (!trimmed) return
  history.value = [trimmed, ...history.value.filter(h => h !== trimmed)].slice(0, 10)
  try { localStorage.setItem(HISTORY_KEY, JSON.stringify(history.value)) } catch { /* ignore */ }
}
function clearHistory() {
  history.value = []
  try { localStorage.removeItem(HISTORY_KEY) } catch { /* ignore */ }
}

/** 点击示例词或历史记录填充输入框 */
function pickExample(text: string) {
  inputText.value = text
  if (mode.value === 'translate') translate()
}

// ── 命名规则生成函数 ──

function toCamelCase(w: string[]): string {
  return w.map((word, i) =>
    i === 0 ? word.toLowerCase() : word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()
  ).join('')
}

function toPascalCase(w: string[]): string {
  return w.map(word => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()).join('')
}

function toFlatCase(w: string[]): string {
  return w.map(word => word.toLowerCase()).join('')
}

function generateAll(w: string[]): NamingItem[] {
  if (w.length === 0) return []
  return [
    { rule: '驼峰 camelCase', value: toCamelCase(w) },
    { rule: '大驼峰 PascalCase', value: toPascalCase(w) },
    { rule: '小写 flatcase', value: toFlatCase(w) },
    { rule: '下划线 snake_case', value: w.join('_') },
    { rule: '短横线 kebab-case', value: w.join('-') },
    { rule: '常量 UPPER_SNAKE', value: w.map(x => x.toUpperCase()).join('_') },
    { rule: '大写短横 SCREAMING-KEBAB', value: w.map(x => x.toUpperCase()).join('-') },
    { rule: '点分隔 dot.case', value: w.join('.') },
    { rule: '路径 path/case', value: w.join('/') },
    { rule: '标题 Title Case', value: w.map(x => x.charAt(0).toUpperCase() + x.slice(1)).join(' ') },
  ]
}

/**
 * 智能拆词：先按英文分隔符拆分，若结果为空（含中文时）自动转拼音。
 * 支持纯英文、纯中文、中英混排（如「用户management」→ yonghu management）。
 */
function smartSplitWords(text: string): string[] {
  if (!text) return []
  // 先拆驼峰："userManagement" → "user Management"，"md5Hash" → "md5 Hash"
  const camelSplit = text.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  // 再按常规分隔符拆分
  const parts = camelSplit.split(/[\s_\-\/\.]+/).map(w => w.trim()).filter(w => w.length > 0)
  const result: string[] = []
  for (const part of parts) {
    if (/^[a-zA-Z0-9]+$/.test(part)) {
      // 纯英文/数字，直接收（如 user、md5、sha256）
      result.push(part.toLowerCase())
    } else if (/^[\u4e00-\u9fa5]+$/.test(part)) {
      // 纯中文，转拼音（以空格分隔每个字的拼音）
      const py = pinyin(part, { toneType: 'none', nonZh: 'consecutive', v: true })
      // pinyin-pro 默认输出如「你好」→「ni hao」，按空格拆分
      for (const w of py.split(/\s+/).map(s => s.trim()).filter(s => s.length > 0)) {
        if (/^[a-zA-Z0-9]+$/.test(w)) result.push(w.toLowerCase())
      }
    } else {
      // 中英混排：逐段拆分
      const segments = part.split(/([\u4e00-\u9fa5]+)/).filter(s => s.length > 0)
      for (const seg of segments) {
        if (/^[a-zA-Z0-9]+$/.test(seg)) {
          result.push(seg.toLowerCase())
        } else if (/^[\u4e00-\u9fa5]+$/.test(seg)) {
          const py = pinyin(seg, { toneType: 'none', nonZh: 'consecutive', v: true })
          for (const w of py.split(/\s+/).map(s => s.trim()).filter(s => s.length > 0)) {
            if (/^[a-zA-Z0-9]+$/.test(w)) result.push(w.toLowerCase())
          }
        }
      }
    }
  }
  return result
}

// ── 缩短版命名（名称过长时提供） ──

/** 缩短版触发阈值：单词拼合后（不含分隔符）超过该长度才生成缩短版 */
const SHORTEN_THRESHOLD = 20

/** 无实义虚词：缩短版中直接省略 */
const STOP_WORDS = new Set(['a', 'an', 'the', 'of', 'for', 'and', 'to', 'in', 'on', 'at', 'by', 'with', 'from', 'or', 'as', 'via'])

/**
 * 常见开发词汇缩写表（键为拆词后的小写单词，长度均 ≥5）。
 * 仅收录业界通用缩写，生僻缩写反而不利于阅读；表外的长单词保持原样。
 */
const WORD_ABBR: Record<string, string> = {
  address: 'addr',
  administrator: 'admin',
  application: 'app',
  argument: 'arg',
  arguments: 'args',
  array: 'arr',
  asynchronous: 'async',
  attribute: 'attr',
  authentication: 'auth',
  authorization: 'auth',
  average: 'avg',
  background: 'bg',
  buffer: 'buf',
  button: 'btn',
  calculate: 'calc',
  calculator: 'calc',
  character: 'char',
  command: 'cmd',
  component: 'comp',
  config: 'cfg',
  configuration: 'cfg',
  connection: 'conn',
  context: 'ctx',
  controller: 'ctrl',
  converter: 'conv',
  current: 'cur',
  dashboard: 'dash',
  database: 'db',
  default: 'def',
  delete: 'del',
  description: 'desc',
  destination: 'dst',
  development: 'dev',
  dictionary: 'dict',
  directory: 'dir',
  document: 'doc',
  element: 'elem',
  environment: 'env',
  error: 'err',
  execute: 'exec',
  executor: 'exec',
  exporter: 'exp',
  extension: 'ext',
  foreground: 'fg',
  function: 'fn',
  generate: 'gen',
  generator: 'gen',
  header: 'hdr',
  history: 'hist',
  identifier: 'id',
  image: 'img',
  importer: 'imp',
  index: 'idx',
  information: 'info',
  instance: 'inst',
  integer: 'int',
  language: 'lang',
  length: 'len',
  library: 'lib',
  management: 'mgr',
  manager: 'mgr',
  maximum: 'max',
  memory: 'mem',
  message: 'msg',
  metadata: 'meta',
  minimum: 'min',
  module: 'mod',
  namespace: 'ns',
  navigation: 'nav',
  notification: 'notify',
  number: 'num',
  object: 'obj',
  package: 'pkg',
  parameter: 'param',
  password: 'pwd',
  payment: 'pay',
  pointer: 'ptr',
  position: 'pos',
  previous: 'prev',
  probability: 'prob',
  process: 'proc',
  property: 'prop',
  reference: 'ref',
  repository: 'repo',
  request: 'req',
  response: 'resp',
  schedule: 'sched',
  scheduler: 'sched',
  segment: 'seg',
  service: 'svc',
  source: 'src',
  statistic: 'stat',
  statistics: 'stats',
  string: 'str',
  synchronize: 'sync',
  synchronization: 'sync',
  system: 'sys',
  template: 'tpl',
  temporary: 'tmp',
  timestamp: 'ts',
  transaction: 'tx',
  variable: 'var',
  version: 'ver',
}

/** 缩写单词：命中缩写表用缩写；复数形式（services）退回单数查表后加 s；短词与表外词保持原样 */
function abbreviateWord(word: string): string {
  if (word.length < 5) return word
  if (WORD_ABBR[word]) return WORD_ABBR[word]
  if (word.endsWith('s') && WORD_ABBR[word.slice(0, -1)]) return WORD_ABBR[word.slice(0, -1)] + 's'
  return word
}

/**
 * 计算缩短版单词列表。原名单词拼合后超过阈值、且省略虚词/缩写后确实更短时返回缩短列表，
 * 否则返回 null（不展示缩短版）。
 */
function computeShortened(w: string[]): string[] | null {
  const full = w.join('')
  if (full.length <= SHORTEN_THRESHOLD) return null
  const shortened = w.filter(word => !STOP_WORDS.has(word)).map(abbreviateWord)
  if (shortened.length === 0 || shortened.join('') === full) return null
  return shortened
}

// ── 单行模式 ──

/** 直接输入模式：API 翻译结果（含中文时自动翻译，纯英文时为空） */
const directTranslated = ref('')

/** 从翻译结果或手动编辑中提取的单词列表 */
const words = computed(() => {
  if (mode.value === 'direct' && !batchMode.value) {
    // 直接输入模式：有翻译结果用翻译，否则用原文（纯英文场景）
    const text = directTranslated.value || editedTranslation.value
    return smartSplitWords(text)
  }
  const text = editedTranslation.value || result.value?.translated || ''
  return smartSplitWords(text)
})

/** 单行命名规则结果 */
const namingResults = computed(() => generateAll(words.value))

/** 缩短版：命名过长时（拼合 > 20 字符）提供省略虚词 + 缩写常用词的方案 */
const shortenedWords = computed(() => computeShortened(words.value) ?? [])

/** 缩短版命名规则结果（无缩短需求时为空数组） */
const shortenedResults = computed(() => shortenedWords.value.length > 0 ? generateAll(shortenedWords.value) : [])

/** 缩短版前后字符数（用于展示说明；无缩短版时为 null） */
const shortenMeta = computed(() =>
  shortenedWords.value.length > 0
    ? { from: words.value.join('').length, to: shortenedWords.value.join('').length }
    : null,
)

const hasResult = computed(() => words.value.length > 0)

// ── 批量模式 ──

/** 批量行结果：异步翻译后更新 */
const batchRows = ref<BatchRow[]>([])

const hasBatchResult = computed(() => batchRows.value.length > 0)

// ── 操作函数 ──

/** 是否正在使用拼音兼底（API 翻译失败时） */
const usingPinyinFallback = ref(false)

/** 翻译模式：输入后自动翻译（500ms 防抖，避免逐字符发请求） */
let translateTimer: ReturnType<typeof setTimeout> | null = null
watch(inputText, (val) => {
  if (mode.value !== 'translate' || batchMode.value) return
  if (translateTimer) clearTimeout(translateTimer)
  const text = val.trim()
  if (!text) { result.value = null; editedTranslation.value = ''; usingPinyinFallback.value = false; return }
  translateTimer = setTimeout(() => translate(), 500)
})

/** 直接输入模式：含中文时自动调翻译 API */
watch(editedTranslation, (val) => {
  if (mode.value !== 'direct' || batchMode.value) return
  if (translateTimer) clearTimeout(translateTimer)
  const text = val.trim()
  if (!text || !/[\u4e00-\u9fa5]/.test(text)) { directTranslated.value = ''; return }
  translateTimer = setTimeout(() => translateDirect(text), 500)
})

/** 批量模式：含中文的行调翻译 API */
watch(batchText, (val) => {
  if (!batchMode.value) return
  if (translateTimer) clearTimeout(translateTimer)
  const text = val.trim()
  if (!text) { batchRows.value = []; return }
  translateTimer = setTimeout(() => translateBatch(val), 500)
})

async function translate() {
  const text = inputText.value.trim()
  if (!text) return
  loading.value = true
  usingPinyinFallback.value = false
  try {
    const res = await httpGet<TranslateResult>('/api/Common/CodeNaming/Translate', { text })
    const hasChinese = /[\u4e00-\u9fa5]/.test(res.translated)
    if (hasChinese || !res.translated) {
      // API 返回中文残留或空，用拼音兼底
      const py = pinyin(text, { toneType: 'none', nonZh: 'consecutive', v: true })
      result.value = { original: text, translated: py, words: smartSplitWords(py) }
      editedTranslation.value = py
      usingPinyinFallback.value = true
    } else {
      result.value = res
      editedTranslation.value = res.translated
    }
    pushHistory(text)
  } catch {
    const py = pinyin(text, { toneType: 'none', nonZh: 'consecutive', v: true })
    result.value = { original: text, translated: py, words: smartSplitWords(py) }
    editedTranslation.value = py
    usingPinyinFallback.value = true
    pushHistory(text)
  } finally {
    loading.value = false
  }
}

/** 直接输入模式：含中文时调翻译 API */
async function translateDirect(text: string) {
  loading.value = true
  try {
    const res = await httpGet<TranslateResult>('/api/Common/CodeNaming/Translate', { text })
    if (res.translated && !/[\u4e00-\u9fa5]/.test(res.translated)) {
      directTranslated.value = res.translated
    } else {
      directTranslated.value = pinyin(text, { toneType: 'none', nonZh: 'consecutive', v: true })
    }
  } catch {
    directTranslated.value = pinyin(text, { toneType: 'none', nonZh: 'consecutive', v: true })
  } finally {
    loading.value = false
  }
}

/** 构建批量行：正常结果 + 过长时的缩短版 */
function buildBatchRow(source: string, w: string[]): BatchRow | null {
  if (w.length === 0) return null
  const row: BatchRow = { source, words: w, results: generateAll(w) }
  const shortened = computeShortened(w)
  if (shortened) row.shortened = { words: shortened, results: generateAll(shortened) }
  return row
}

/** 批量模式：并行翻译含中文的行 */
async function translateBatch(text: string) {
  loading.value = true
  const lines = text.split('\n').map(l => l.trim()).filter(l => l.length > 0)
  const promises = lines.map(async (line) => {
    if (!/[\u4e00-\u9fa5]/.test(line)) {
      return buildBatchRow(line, smartSplitWords(line))
    }
    try {
      const res = await httpGet<TranslateResult>('/api/Common/CodeNaming/Translate', { text: line })
      const translated = (res.translated && !/[\u4e00-\u9fa5]/.test(res.translated))
        ? res.translated
        : pinyin(line, { toneType: 'none', nonZh: 'consecutive', v: true })
      return buildBatchRow(line, smartSplitWords(translated))
    } catch {
      const py = pinyin(line, { toneType: 'none', nonZh: 'consecutive', v: true })
      return buildBatchRow(line, smartSplitWords(py))
    }
  })
  const results = await Promise.all(promises)
  batchRows.value = results.filter((r): r is BatchRow => r !== null)
  loading.value = false
}

async function copyText(text: string, label?: string) {
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(label ? `已复制：${label}` : '已复制')
  } catch {
    const ta = document.createElement('textarea')
    ta.value = text
    ta.style.position = 'fixed'
    ta.style.opacity = '0'
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success(label ? `已复制：${label}` : '已复制')
  }
}

/** 复制全部命名规则（单行模式，含缩短版） */
function copyAllSingle() {
  const lines = namingResults.value.map(r => `${r.rule}: ${r.value}`)
  if (shortenedResults.value.length > 0) {
    lines.push('', '【缩短版】')
    for (const r of shortenedResults.value) lines.push(`${r.rule}: ${r.value}`)
  }
  copyText(lines.join('\n'), '全部命名规则')
}

/** 复制缩短版全部命名规则 */
function copyAllShortened() {
  const text = shortenedResults.value.map(r => `${r.rule}: ${r.value}`).join('\n')
  copyText(text, '缩短版命名规则')
}

/** 复制指定批量行的全部命名规则（含缩短版） */
function copyBatchRow(row: BatchRow) {
  const lines = row.results.map(r => `${r.rule}: ${r.value}`)
  if (row.shortened) {
    lines.push('', '【缩短版】')
    for (const r of row.shortened.results) lines.push(`${r.rule}: ${r.value}`)
  }
  copyText(lines.join('\n'), row.source)
}

/** 复制全部批量行 */
function copyAllBatch() {
  const text = batchRows.value
    .map(row => {
      const lines = row.results.map(r => `  ${r.rule}: ${r.value}`)
      if (row.shortened) {
        lines.push('', '  【缩短版】')
        for (const r of row.shortened.results) lines.push(`  ${r.rule}: ${r.value}`)
      }
      return `# ${row.source}\n${lines.join('\n')}`
    })
    .join('\n\n')
  copyText(text, '全部批量结果')
}

/** 切换模式时清空 */
function onModeChange() {
  result.value = null
  editedTranslation.value = ''
  inputText.value = ''
  usingPinyinFallback.value = false
  directTranslated.value = ''
  batchRows.value = []
  batchText.value = ''
}

/** 切换单行/批量时清空 */
function onBatchToggle() {
  inputText.value = ''
  batchText.value = ''
  result.value = null
  editedTranslation.value = ''
  usingPinyinFallback.value = false
  directTranslated.value = ''
  batchRows.value = []
  expandedRows.value.clear()
}

/** 批量折叠状态 */
const expandedRows = ref<Set<number>>(new Set())

/** 展开/收起单个批量块 */
function toggleBatchRow(ri: number) {
  if (expandedRows.value.has(ri)) expandedRows.value.delete(ri)
  else expandedRows.value.add(ri)
  // 触发响应式更新
  expandedRows.value = new Set(expandedRows.value)
}

/** 展开全部 */
function expandAllRows() {
  expandedRows.value = new Set(batchRows.value.map((_, i) => i))
}

/** 收起全部 */
function collapseAllRows() {
  expandedRows.value = new Set()
}
</script>

<template>
  <div class="naming-page">
    <!-- 模式切换 -->
    <div class="naming-toolbar">
      <el-radio-group v-model="mode" size="default" @change="onModeChange">
        <el-radio-button value="translate">中文翻译</el-radio-button>
        <el-radio-button value="direct">直接输入</el-radio-button>
      </el-radio-group>
      <el-radio-group v-model="batchMode" size="default" style="margin-left: 12px;" @change="onBatchToggle">
        <el-radio-button :value="false">单行</el-radio-button>
        <el-radio-button :value="true">批量</el-radio-button>
      </el-radio-group>
    </div>

    <!-- ════ 中文翻译 · 单行 ════ -->
    <template v-if="mode === 'translate' && !batchMode">
      <div class="naming-input-section">
        <el-input
          v-model="inputText"
          placeholder="输入中文描述，自动翻译（如：用户管理、订单查询）"
          size="large"
          clearable
        >
          <template #prepend>中文</template>
          <template #suffix>
            <el-icon v-if="loading" class="is-loading"><Loading /></el-icon>
          </template>
        </el-input>
      </div>

      <!-- 示例词 + 历史记录 -->
      <div v-if="EXAMPLES.length || history.length" class="naming-chips">
        <div class="chips-group" v-if="EXAMPLES.length">
          <span class="chips-label">示例</span>
          <el-tag v-for="ex in EXAMPLES" :key="ex" size="small" effect="plain" class="chip" @click="pickExample(ex)">{{ ex }}</el-tag>
        </div>
        <div class="chips-group" v-if="history.length">
          <span class="chips-label">历史</span>
          <el-tag v-for="h in history" :key="h" size="small" type="info" effect="plain" class="chip" @click="pickExample(h)">{{ h }}</el-tag>
          <el-button size="small" text @click="clearHistory">清空</el-button>
        </div>
      </div>

      <div v-if="result" class="naming-translate-section">
        <span class="section-label">英文</span>
        <el-input v-model="editedTranslation" placeholder="翻译结果（可修正，下方实时更新）" size="default" clearable />
        <el-tag v-if="usingPinyinFallback" size="small" type="warning" effect="plain">拼音兼底</el-tag>
      </div>

      <div v-if="hasResult" class="naming-results">
        <div class="results-header">
          <span class="section-label">命名规则</span>
          <el-button size="small" text type="primary" @click="copyAllSingle">
            <el-icon style="margin-right: 4px;"><DocumentCopy /></el-icon>复制全部
          </el-button>
        </div>
        <div class="words-display">
          <span class="words-label">拆词</span>
          <el-tag v-for="w in words" :key="w" size="small" type="info" effect="plain">{{ w }}</el-tag>
        </div>
        <div v-for="item in namingResults" :key="item.rule" class="result-row">
          <span class="rule-name">{{ item.rule }}</span>
          <span class="rule-value" @click="copyText(item.value, item.rule)">{{ item.value }}</span>
          <el-button size="small" text class="copy-btn" @click="copyText(item.value, item.rule)">
            <el-icon><CopyDocument /></el-icon>
          </el-button>
        </div>
      </div>

      <!-- 缩短版：原命名过长时提供 -->
      <div v-if="shortenMeta" class="naming-results shortened">
        <div class="results-header">
          <span class="section-label">缩短版命名</span>
          <span class="shorten-note">原名 {{ shortenMeta.from }} 字符 → 缩短后 {{ shortenMeta.to }} 字符（已省略虚词、缩写常用词）</span>
          <el-button size="small" text type="primary" @click="copyAllShortened">
            <el-icon style="margin-right: 4px;"><DocumentCopy /></el-icon>复制全部
          </el-button>
        </div>
        <div class="words-display">
          <span class="words-label">缩短拆词</span>
          <el-tag v-for="w in shortenedWords" :key="w" size="small" type="warning" effect="plain">{{ w }}</el-tag>
        </div>
        <div v-for="item in shortenedResults" :key="item.rule" class="result-row">
          <span class="rule-name">{{ item.rule }}</span>
          <span class="rule-value" @click="copyText(item.value, `缩短版 ${item.rule}`)">{{ item.value }}</span>
          <el-button size="small" text class="copy-btn" @click="copyText(item.value, `缩短版 ${item.rule}`)">
            <el-icon><CopyDocument /></el-icon>
          </el-button>
        </div>
      </div>
    </template>

    <!-- ════ 直接输入 · 单行 ════ -->
    <template v-if="mode === 'direct' && !batchMode">
      <div class="naming-input-section">
        <el-input
          v-model="editedTranslation"
          placeholder="输入英文或中文均可（如：user management 或 用户管理）"
          size="large"
          clearable
        >
          <template #prepend>输入</template>
          <template #suffix>
            <el-icon v-if="loading" class="is-loading"><Loading /></el-icon>
          </template>
        </el-input>
      </div>

      <div v-if="hasResult" class="naming-results">
        <div class="results-header">
          <span class="section-label">命名规则</span>
          <el-button size="small" text type="primary" @click="copyAllSingle">
            <el-icon style="margin-right: 4px;"><DocumentCopy /></el-icon>复制全部
          </el-button>
        </div>
        <div class="words-display">
          <span class="words-label">拆词</span>
          <el-tag v-for="w in words" :key="w" size="small" type="info" effect="plain">{{ w }}</el-tag>
        </div>
        <div v-for="item in namingResults" :key="item.rule" class="result-row">
          <span class="rule-name">{{ item.rule }}</span>
          <span class="rule-value" @click="copyText(item.value, item.rule)">{{ item.value }}</span>
          <el-button size="small" text class="copy-btn" @click="copyText(item.value, item.rule)">
            <el-icon><CopyDocument /></el-icon>
          </el-button>
        </div>
      </div>

      <!-- 缩短版：原命名过长时提供 -->
      <div v-if="shortenMeta" class="naming-results shortened">
        <div class="results-header">
          <span class="section-label">缩短版命名</span>
          <span class="shorten-note">原名 {{ shortenMeta.from }} 字符 → 缩短后 {{ shortenMeta.to }} 字符（已省略虚词、缩写常用词）</span>
          <el-button size="small" text type="primary" @click="copyAllShortened">
            <el-icon style="margin-right: 4px;"><DocumentCopy /></el-icon>复制全部
          </el-button>
        </div>
        <div class="words-display">
          <span class="words-label">缩短拆词</span>
          <el-tag v-for="w in shortenedWords" :key="w" size="small" type="warning" effect="plain">{{ w }}</el-tag>
        </div>
        <div v-for="item in shortenedResults" :key="item.rule" class="result-row">
          <span class="rule-name">{{ item.rule }}</span>
          <span class="rule-value" @click="copyText(item.value, `缩短版 ${item.rule}`)">{{ item.value }}</span>
          <el-button size="small" text class="copy-btn" @click="copyText(item.value, `缩短版 ${item.rule}`)">
            <el-icon><CopyDocument /></el-icon>
          </el-button>
        </div>
      </div>
    </template>

    <!-- ════ 批量模式 ════ -->
    <template v-if="batchMode">
      <div class="batch-input-section">
        <el-input
          v-model="batchText"
          type="textarea"
          :rows="6"
          placeholder="每行一个描述，中英文均可，实时生成全部命名规则：&#10;用户管理&#10;orderQuery&#10;数据导出"
          clearable
        />
      </div>

      <div v-if="hasBatchResult" class="batch-toolbar">
        <span class="section-label">共 {{ batchRows.length }} 行结果</span>
        <div class="batch-toolbar-actions">
          <el-icon v-if="loading" class="is-loading" style="margin-right: 4px;"><Loading /></el-icon>
          <el-button size="small" text @click="expandAllRows">展开全部</el-button>
          <el-button size="small" text @click="collapseAllRows">收起全部</el-button>
          <el-button size="small" text type="primary" @click="copyAllBatch">
            <el-icon style="margin-right: 4px;"><DocumentCopy /></el-icon>复制全部
          </el-button>
        </div>
      </div>

      <div v-for="(row, ri) in batchRows" :key="ri" class="batch-block">
        <div class="batch-header" @click="toggleBatchRow(ri)">
          <el-icon class="toggle-icon" :class="{ expanded: expandedRows.has(ri) }"><ArrowRight /></el-icon>
          <span class="batch-source">{{ row.source }}</span>
          <span class="batch-preview">{{ row.results[0]?.value }}</span>
          <el-button size="small" text class="copy-btn" @click.stop="copyBatchRow(row)">
            <el-icon><DocumentCopy /></el-icon>
          </el-button>
        </div>
        <el-collapse-transition>
          <div v-show="expandedRows.has(ri)">
            <div v-for="item in row.results" :key="item.rule" class="result-row compact">
              <span class="rule-name">{{ item.rule }}</span>
              <span class="rule-value" @click="copyText(item.value, item.rule)">{{ item.value }}</span>
              <el-button size="small" text class="copy-btn" @click="copyText(item.value, item.rule)">
                <el-icon><CopyDocument /></el-icon>
              </el-button>
            </div>
            <div v-if="row.shortened" class="batch-shortened">
              <div class="batch-shortened-title">
                缩短版命名（原名 {{ row.words.join('').length }} 字符 → 缩短后 {{ row.shortened.words.join('').length }} 字符）
              </div>
              <div v-for="item in row.shortened.results" :key="item.rule" class="result-row compact">
                <span class="rule-name">{{ item.rule }}</span>
                <span class="rule-value" @click="copyText(item.value, `缩短版 ${item.rule}`)">{{ item.value }}</span>
                <el-button size="small" text class="copy-btn" @click="copyText(item.value, `缩短版 ${item.rule}`)">
                  <el-icon><CopyDocument /></el-icon>
                </el-button>
              </div>
            </div>
          </div>
        </el-collapse-transition>
      </div>
    </template>

    <!-- 空状态 -->
    <div v-if="!hasResult && !hasBatchResult && !loading" class="naming-empty">
      <el-icon :size="48" color="#dcdfe6"><EditPen /></el-icon>
      <p v-if="mode === 'translate' && !batchMode">输入中文描述自动翻译，或点击上方示例词</p>
      <p v-else-if="mode === 'direct' && !batchMode">输入英文或中文均可，实时生成全部命名规则</p>
      <p v-else>每行输入一个描述（中英文均可），实时生成批量命名结果</p>
    </div>
  </div>
</template>

<style scoped>
.naming-page {
  max-width: 820px;
  margin: 0 auto;
  padding: 24px 16px;
  /* height:100% + 自身滚动（非依赖外层）：主窗口 .layout-main 与独立窗口
     .standalone-page（100vh+overflow:hidden）下父容器高度均确定，页面自身接管滚动 */
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}

.naming-toolbar {
  display: flex;
  align-items: center;
  margin-bottom: 20px;
}

.naming-input-section {
  display: flex;
  align-items: center;
  margin-bottom: 20px;
}

.naming-input-section .el-input {
  flex: 1;
}

/* ── 示例词 + 历史记录 chips ── */
.naming-chips {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 20px;
}

.chips-group {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.chips-label {
  font-size: 12px;
  color: #909399;
  margin-right: 4px;
}

.chip {
  cursor: pointer;
  transition: all 0.15s;
}

.chip:hover {
  transform: translateY(-1px);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.08);
}

.naming-translate-section {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 24px;
}

.naming-translate-section .el-input {
  flex: 1;
}

.batch-input-section {
  margin-bottom: 20px;
}

.batch-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.batch-toolbar-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}

.section-label {
  font-size: 13px;
  color: #909399;
  white-space: nowrap;
  min-width: 60px;
}

.naming-results {
  border: 1px solid #ebeef5;
  border-radius: 8px;
  overflow: hidden;
}

.results-header {
  display: flex;
  align-items: center;
  padding: 8px 16px;
  background: #f5f7fa;
  border-bottom: 1px solid #ebeef5;
}

.results-header .section-label {
  flex: 1;
}

/* ── 拆词展示 ── */
.words-display {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 16px;
  background: #fafafa;
  border-bottom: 1px solid #f0f0f0;
}

.words-label {
  font-size: 12px;
  color: #909399;
  margin-right: 4px;
}

.result-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid #f0f0f0;
  transition: background 0.15s;
}

.result-row:last-child {
  border-bottom: none;
}

.result-row:hover {
  background: #f9fafc;
}

.result-row.compact {
  padding: 6px 16px;
}

.rule-name {
  font-size: 13px;
  color: #606266;
  white-space: nowrap;
  min-width: 150px;
}

.rule-value {
  flex: 1;
  font-family: 'Cascadia Code', 'Consolas', 'Courier New', monospace;
  font-size: 14px;
  color: #303133;
  background: #f5f7fa;
  padding: 4px 10px;
  border-radius: 4px;
  word-break: break-all;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.rule-value:hover {
  background: #e8f4ff;
  color: #409eff;
}

/* ── 复制按钮悬浮变色 ── */
.copy-btn {
  color: #c0c4cc;
  transition: color 0.2s, background 0.2s, transform 0.15s;
  flex-shrink: 0;
}

.copy-btn:hover {
  color: #409eff;
  background: #ecf5ff;
  transform: scale(1.15);
}

.copy-btn:active {
  transform: scale(0.95);
}

/* ── 批量模式 ── */
.batch-block {
  border: 1px solid #ebeef5;
  border-radius: 8px;
  overflow: hidden;
  margin-bottom: 16px;
}

.batch-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  background: #f5f7fa;
  border-bottom: 1px solid #ebeef5;
  cursor: pointer;
  transition: background 0.15s;
}

.batch-header:hover {
  background: #ecf5ff;
}

.toggle-icon {
  color: #909399;
  transition: transform 0.2s;
  flex-shrink: 0;
}

.toggle-icon.expanded {
  transform: rotate(90deg);
}

.batch-preview {
  font-family: 'Cascadia Code', 'Consolas', 'Courier New', monospace;
  font-size: 13px;
  color: #606266;
  background: #e8f4ff;
  padding: 2px 8px;
  border-radius: 4px;
}

.batch-source {
  flex: 1;
  font-size: 13px;
  font-weight: 600;
  color: #303133;
  font-family: 'Cascadia Code', 'Consolas', monospace;
}

.naming-empty {
  text-align: center;
  padding: 60px 0;
  color: #c0c4cc;
}

.naming-empty p {
  margin-top: 12px;
  font-size: 13px;
}

/* ── 缩短版命名 ── */
.naming-results.shortened {
  margin-top: 12px;
}

.naming-results.shortened .results-header {
  background: #fdf6ec;
}

.shorten-note {
  flex: 1;
  font-size: 12px;
  color: #b88230;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.batch-shortened-title {
  padding: 6px 16px;
  font-size: 12px;
  color: #b88230;
  background: #fdf6ec;
  border-top: 1px solid #f0f0f0;
}
</style>
