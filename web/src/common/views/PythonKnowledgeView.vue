<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, nextTick, watch } from 'vue'
import { marked } from 'marked'
import hljs from 'highlight.js/lib/core'
import python from 'highlight.js/lib/languages/python'
import bash from 'highlight.js/lib/languages/bash'
import sql from 'highlight.js/lib/languages/sql'
import dockerfile from 'highlight.js/lib/languages/dockerfile'
import yaml from 'highlight.js/lib/languages/yaml'
import plaintext from 'highlight.js/lib/languages/plaintext'
import 'highlight.js/styles/github.css'
import knowledgeRaw from '../../../../python/Python知识库.md?raw'

// 注册语言
hljs.registerLanguage('python', python)
hljs.registerLanguage('python3', python)
hljs.registerLanguage('bash', bash)
hljs.registerLanguage('shell', bash)
hljs.registerLanguage('sql', sql)
hljs.registerLanguage('dockerfile', dockerfile)
hljs.registerLanguage('yaml', yaml)
hljs.registerLanguage('dockerfile', dockerfile)
hljs.registerLanguage('plaintext', plaintext)
hljs.registerLanguage('text', plaintext)

// ========== Markdown 渲染（含代码高亮 + 复制按钮） ==========
const renderer = new marked.Renderer()

// 代码块渲染：语法高亮 + 语言标签 + 复制按钮
renderer.code = function (token: { text: string; lang?: string }) {
  const code = token.text || ''
  const language = token.lang || 'text'
  let highlighted: string
  try {
    highlighted = hljs.getLanguage(language)
      ? hljs.highlight(code, { language }).value
      : hljs.highlight(code, { language: 'python' }).value
  } catch {
    highlighted = code.replace(/</g, '&lt;').replace(/>/g, '&gt;')
  }
  return `<div class="code-block">
    <div class="code-header">
      <span class="code-lang">${language}</span>
      <button class="code-copy-btn" onclick="(function(btn){var pre=btn.closest('.code-block').querySelector('code');navigator.clipboard.writeText(pre.innerText).then(function(){btn.textContent='已复制';btn.classList.add('copied');setTimeout(function(){btn.textContent='复制';btn.classList.remove('copied')},1500)})})(this)">复制</button>
    </div>
    <pre><code class="hljs language-${language}">${highlighted}</code></pre>
  </div>`
}

// 标题渲染：给 h2/h3 添加 id 用于锚点跳转
renderer.heading = function (token: { text: string; depth: number }) {
  const text = typeof token.text === 'string' ? token.text : String(token.text ?? '')
  const depth = token.depth ?? 2
  const slug = text.replace(/<[^>]*>/g, '').replace(/\s+/g, '-').replace(/[^\w\u4e00-\u9fff-]/g, '').toLowerCase()
  return `<h${depth} id="${slug}">${text}<a class="heading-anchor" data-anchor="${slug}">#</a></h${depth}>`
}

// 链接渲染：外部链接新标签页打开，内部锚点阻止默认跳转
renderer.link = function (token: { href?: string; text?: string; title?: string | null }) {
  const href = token.href || ''
  const text = token.text || ''
  const title = token.title ? ` title="${token.title}"` : ''
  if (href.startsWith('http://') || href.startsWith('https://')) {
    return `<a href="${href}" target="_blank" rel="noopener noreferrer"${title}>${text}</a>`
  }
  // 内部锚点链接：用 data 属性标记，点击时由事件委托处理
  if (href.startsWith('#')) {
    const anchor = href.slice(1)
    return `<a href="javascript:void(0)" data-anchor="${anchor}"${title}>${text}</a>`
  }
  return `<a href="${href}"${title}>${text}</a>`
}

marked.setOptions({ renderer, gfm: true, breaks: false })
const htmlContent = computed(() => marked.parse(knowledgeRaw) as string)

// ========== 目录（h2 + h3 两级） ==========
interface TocItem {
  id: string
  text: string
  level: number
  children: TocItem[]
}
const tocItems = ref<TocItem[]>([])
const activeTocId = ref('')

function buildToc() {
  const content = document.querySelector('.knowledge-content')
  if (!content) return
  const headings = content.querySelectorAll<HTMLHeadingElement>('h2, h3')
  const items: TocItem[] = []
  let current: TocItem | null = null

  headings.forEach((h) => {
    const id = h.id || h.textContent?.replace(/\s+/g, '-').replace(/[^\w\u4e00-\u9fff-]/g, '').toLowerCase() || ''
    h.id = id
    const item: TocItem = { id, text: h.textContent?.replace('#', '').trim() || '', level: parseInt(h.tagName[1]), children: [] }
    if (h.tagName === 'H2') {
      items.push(item)
      current = item
    } else if (h.tagName === 'H3' && current) {
      current.children.push(item)
    }
  })
  tocItems.value = items
}

// ========== 滚动监听：目录高亮跟随 + 阅读进度 ==========
const readingProgress = ref(0)
const showBackTop = ref(false)
let contentEl: HTMLElement | null = null

function onContentScroll() {
  if (!contentEl) return
  const { scrollTop, scrollHeight, clientHeight } = contentEl
  // 阅读进度
  readingProgress.value = scrollHeight > clientHeight ? Math.round((scrollTop / (scrollHeight - clientHeight)) * 100) : 0
  // 回到顶部按钮
  showBackTop.value = scrollTop > 300
  // 目录高亮：找当前视口内最顶部的标题
  const headings = contentEl.querySelectorAll<HTMLHeadingElement>('h2[id], h3[id]')
  let activeId = ''
  for (const h of headings) {
    if (h.getBoundingClientRect().top <= 80) {
      activeId = h.id
    }
  }
  activeTocId.value = activeId
}

function scrollToHeading(id: string) {
  if (!contentEl) return
  const el = contentEl.querySelector(`#${CSS.escape(id)}`) as HTMLElement
  if (el) {
    contentEl.scrollTo({ top: el.offsetTop - 16, behavior: 'smooth' })
  }
}

function scrollToTop() {
  contentEl?.scrollTo({ top: 0, behavior: 'smooth' })
}

// 内容区点击事件委托：处理锚点链接跳转
function onContentClick(e: MouseEvent) {
  const target = (e.target as HTMLElement).closest('a[data-anchor]') as HTMLElement | null
  if (!target) return
  e.preventDefault()
  const anchor = target.dataset.anchor
  if (anchor) scrollToHeading(anchor)
}

// ========== 搜索 ==========
const searchKeyword = ref('')
const searchResults = ref<{ id: string; text: string; context: string }[]>([])
const showSearch = ref(false)

function doSearch() {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) { searchResults.value = []; return }
  const content = document.querySelector('.knowledge-content')
  if (!content) return
  const headings = content.querySelectorAll<HTMLHeadingElement>('h2[id], h3[id]')
  const results: { id: string; text: string; context: string }[] = []
  headings.forEach((h) => {
    // 取标题到下一个标题之间的文本
    let contextText = ''
    let sibling = h.nextElementSibling
    while (sibling && !['H1', 'H2', 'H3'].includes(sibling.tagName)) {
      contextText += sibling.textContent + ' '
      sibling = sibling.nextElementSibling
    }
    const fullText = (h.textContent + ' ' + contextText).toLowerCase()
    if (fullText.includes(kw)) {
      // 提取上下文片段
      const idx = contextText.toLowerCase().indexOf(kw)
      let snippet = ''
      if (idx >= 0) {
        const start = Math.max(0, idx - 30)
        const end = Math.min(contextText.length, idx + kw.length + 50)
        snippet = (start > 0 ? '...' : '') + contextText.slice(start, end).trim() + (end < contextText.length ? '...' : '')
      }
      results.push({ id: h.id, text: h.textContent?.replace('#', '').trim() || '', context: snippet })
    }
  })
  searchResults.value = results
}

watch(searchKeyword, () => { doSearch() })

function hideSearchDelayed() {
  setTimeout(() => { showSearch.value = false }, 200)
}

// ========== 字号控制 ==========
const fontSize = ref(15)
function changeFontSize(delta: number) {
  fontSize.value = Math.max(12, Math.min(20, fontSize.value + delta))
}

// ========== 生命周期 ==========
onMounted(async () => {
  await nextTick()
  contentEl = document.querySelector('.knowledge-content')
  if (contentEl) {
    contentEl.addEventListener('scroll', onContentScroll, { passive: true })
  }
  buildToc()
  // 初次触发一次滚动计算
  onContentScroll()
})

onUnmounted(() => {
  if (contentEl) {
    contentEl.removeEventListener('scroll', onContentScroll)
  }
})
</script>

<template>
  <div class="knowledge-page">
    <!-- 顶部工具栏 -->
    <div class="top-bar">
      <div class="top-bar-left">
        <el-icon class="top-bar-icon"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/></svg></el-icon>
        <span class="top-bar-title">Python 知识库</span>
        <el-tag size="small" type="info">Markdown</el-tag>
      </div>
      <div class="top-bar-center">
        <el-input
          v-model="searchKeyword"
          placeholder="搜索知识点..."
          clearable
          prefix-icon="Search"
          style="width: 280px"
          @focus="showSearch = true"
          @blur="hideSearchDelayed"
        />
        <!-- 搜索结果下拉 -->
        <div v-if="showSearch && searchResults.length > 0" class="search-dropdown">
          <div
            v-for="r in searchResults"
            :key="r.id"
            class="search-item"
            @mousedown.prevent="scrollToHeading(r.id); showSearch = false"
          >
            <div class="search-item-title">{{ r.text }}</div>
            <div class="search-item-context" v-if="r.context">{{ r.context }}</div>
          </div>
        </div>
        <div v-if="showSearch && searchKeyword && searchResults.length === 0" class="search-dropdown search-empty">
          未找到相关内容
        </div>
      </div>
      <div class="top-bar-right">
        <el-tooltip content="缩小字号" placement="bottom">
          <el-button size="small" text @click="changeFontSize(-1)">A-</el-button>
        </el-tooltip>
        <span class="font-size-label">{{ fontSize }}px</span>
        <el-tooltip content="放大字号" placement="bottom">
          <el-button size="small" text @click="changeFontSize(1)">A+</el-button>
        </el-tooltip>
        <el-divider direction="vertical" />
        <el-tooltip content="阅读进度" placement="bottom">
          <span class="progress-label">{{ readingProgress }}%</span>
        </el-tooltip>
      </div>
    </div>

    <!-- 阅读进度条 -->
    <div class="progress-bar">
      <div class="progress-bar-fill" :style="{ width: readingProgress + '%' }" />
    </div>

    <div class="knowledge-body">
      <!-- 左侧目录 -->
      <aside class="toc-sidebar">
        <div class="toc-title">
          <span>目录导航</span>
          <span class="toc-count">{{ tocItems.length }} 章</span>
        </div>
        <nav class="toc-list">
          <template v-for="item in tocItems" :key="item.id">
            <a
              class="toc-item toc-h2"
              :class="{ active: activeTocId === item.id }"
              @click.prevent="scrollToHeading(item.id)"
            >
              {{ item.text }}
            </a>
            <a
              v-for="child in item.children"
              :key="child.id"
              class="toc-item toc-h3"
              :class="{ active: activeTocId === child.id }"
              @click.prevent="scrollToHeading(child.id)"
            >
              {{ child.text }}
            </a>
          </template>
        </nav>
      </aside>

      <!-- 右侧内容 -->
      <main class="knowledge-content" :style="{ fontSize: fontSize + 'px' }" v-html="htmlContent" @click="onContentClick" />
    </div>

    <!-- 回到顶部 -->
    <transition name="fade">
      <div v-if="showBackTop" class="back-top" @click="scrollToTop">
        <el-icon :size="20"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M7.41 15.41L12 10.83l4.59 4.58L18 14l-6-6-6 6z"/></svg></el-icon>
      </div>
    </transition>
  </div>
</template>

<style scoped>
.knowledge-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  position: relative;
}

/* ========== 顶部工具栏 ========== */
.top-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  background: var(--el-bg-color);
  flex-shrink: 0;
  z-index: 10;
}

.top-bar-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.top-bar-icon {
  color: var(--el-color-primary);
  font-size: 20px;
}

.top-bar-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.top-bar-center {
  position: relative;
}

.search-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  margin-top: 4px;
  background: var(--el-bg-color-overlay);
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  box-shadow: var(--el-box-shadow-light);
  max-height: 320px;
  overflow-y: auto;
  z-index: 100;
}

.search-item {
  padding: 10px 14px;
  cursor: pointer;
  border-bottom: 1px solid var(--el-border-color-extra-light);
}

.search-item:last-child {
  border-bottom: none;
}

.search-item:hover {
  background: var(--el-fill-color-light);
}

.search-item-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--el-text-color-primary);
}

.search-item-context {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-top: 4px;
  line-height: 1.4;
}

.search-empty {
  padding: 16px;
  text-align: center;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.top-bar-right {
  display: flex;
  align-items: center;
  gap: 4px;
}

.font-size-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  min-width: 36px;
  text-align: center;
}

.progress-label {
  font-size: 12px;
  color: var(--el-color-primary);
  font-weight: 500;
}

/* ========== 阅读进度条 ========== */
.progress-bar {
  height: 3px;
  background: var(--el-fill-color-light);
  flex-shrink: 0;
}

.progress-bar-fill {
  height: 100%;
  background: var(--el-color-primary);
  transition: width 0.15s ease;
  border-radius: 0 2px 2px 0;
}

/* ========== 主体区域 ========== */
.knowledge-body {
  display: flex;
  flex: 1;
  overflow: hidden;
}

/* ========== 左侧目录 ========== */
.toc-sidebar {
  width: 260px;
  min-width: 260px;
  border-right: 1px solid var(--el-border-color-lighter);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--el-bg-color);
}

.toc-title {
  padding: 14px 16px 10px;
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  border-bottom: 1px solid var(--el-border-color-lighter);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.toc-count {
  font-size: 11px;
  font-weight: 400;
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color);
  padding: 2px 8px;
  border-radius: 10px;
}

.toc-list {
  flex: 1;
  overflow-y: auto;
  padding: 6px 0;
}

.toc-item {
  display: block;
  padding: 7px 16px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  text-decoration: none;
  transition: all 0.15s;
  line-height: 1.4;
  border-left: 3px solid transparent;
}

.toc-h2 {
  font-weight: 500;
}

.toc-h3 {
  padding-left: 28px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.toc-item:hover {
  background-color: var(--el-fill-color-light);
  color: var(--el-color-primary);
}

.toc-item.active {
  color: var(--el-color-primary);
  background-color: var(--el-color-primary-light-9);
  border-left-color: var(--el-color-primary);
  font-weight: 500;
}

/* ========== 右侧内容区 ========== */
.knowledge-content {
  flex: 1;
  overflow-y: auto;
  padding: 24px 40px 60px;
  line-height: 1.8;
  color: var(--el-text-color-primary);
  scroll-behavior: smooth;
}

/* ========== Markdown 渲染样式 ========== */
.knowledge-content :deep(h1) {
  font-size: 1.9em;
  font-weight: 700;
  margin: 0 0 20px;
  padding-bottom: 12px;
  border-bottom: 2px solid var(--el-color-primary);
  position: relative;
}

.knowledge-content :deep(h2) {
  font-size: 1.5em;
  font-weight: 600;
  margin: 40px 0 16px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  scroll-margin-top: 16px;
  position: relative;
}

.knowledge-content :deep(h3) {
  font-size: 1.25em;
  font-weight: 600;
  margin: 28px 0 12px;
  scroll-margin-top: 16px;
}

.knowledge-content :deep(h4) {
  font-size: 1.1em;
  font-weight: 600;
  margin: 20px 0 8px;
}

/* 标题锚点 */
.knowledge-content :deep(.heading-anchor) {
  font-size: 0.75em;
  color: var(--el-color-primary-light-5);
  text-decoration: none;
  margin-left: 6px;
  opacity: 0;
  transition: opacity 0.2s;
}

.knowledge-content :deep(h1:hover .heading-anchor),
.knowledge-content :deep(h2:hover .heading-anchor),
.knowledge-content :deep(h3:hover .heading-anchor) {
  opacity: 1;
}

.knowledge-content :deep(p) {
  margin: 0 0 14px;
}

.knowledge-content :deep(ul),
.knowledge-content :deep(ol) {
  padding-left: 24px;
  margin: 0 0 14px;
}

.knowledge-content :deep(li) {
  margin-bottom: 4px;
}

/* ========== 代码块（增强） ========== */
.knowledge-content :deep(.code-block) {
  margin: 16px 0;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
}

.knowledge-content :deep(.code-header) {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 12px;
  background: var(--el-fill-color);
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.knowledge-content :deep(.code-lang) {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  text-transform: uppercase;
  font-weight: 500;
  letter-spacing: 0.5px;
}

.knowledge-content :deep(.code-copy-btn) {
  font-size: 11px;
  padding: 2px 10px;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  background: var(--el-bg-color);
  color: var(--el-text-color-regular);
  cursor: pointer;
  transition: all 0.15s;
}

.knowledge-content :deep(.code-copy-btn:hover) {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
}

.knowledge-content :deep(.code-copy-btn.copied) {
  border-color: var(--el-color-success);
  color: var(--el-color-success);
}

.knowledge-content :deep(pre) {
  margin: 0;
  padding: 16px;
  overflow-x: auto;
  font-size: 13px;
  line-height: 1.6;
  background: #fafbfc;
}

.knowledge-content :deep(code) {
  font-family: 'Consolas', 'Monaco', 'Fira Code', 'Courier New', monospace;
  font-size: 13px;
}

.knowledge-content :deep(pre code) {
  background: none;
  padding: 0;
  border-radius: 0;
  color: inherit;
}

/* 行内代码 */
.knowledge-content :deep(:not(pre) > code) {
  background-color: var(--el-fill-color);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.88em;
  color: var(--el-color-primary);
  border: 1px solid var(--el-border-color-lighter);
}

/* ========== 表格 ========== */
.knowledge-content :deep(table) {
  width: 100%;
  border-collapse: collapse;
  margin: 16px 0;
  font-size: 14px;
  border-radius: 6px;
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
}

.knowledge-content :deep(th),
.knowledge-content :deep(td) {
  padding: 10px 14px;
  text-align: left;
  border: 1px solid var(--el-border-color-lighter);
}

.knowledge-content :deep(th) {
  background-color: var(--el-fill-color-light);
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.knowledge-content :deep(tr:nth-child(even)) {
  background-color: var(--el-fill-color-lighter);
}

.knowledge-content :deep(tr:hover) {
  background-color: var(--el-color-primary-light-9);
}

/* ========== 引用块 ========== */
.knowledge-content :deep(blockquote) {
  margin: 16px 0;
  padding: 14px 18px;
  border-left: 4px solid var(--el-color-primary);
  background-color: var(--el-color-primary-light-9);
  border-radius: 0 6px 6px 0;
  color: var(--el-text-color-regular);
}

.knowledge-content :deep(blockquote p) {
  margin: 0;
}

/* ========== 分割线 ========== */
.knowledge-content :deep(hr) {
  border: none;
  border-top: 1px solid var(--el-border-color-lighter);
  margin: 32px 0;
}

/* ========== 链接 ========== */
.knowledge-content :deep(a) {
  color: var(--el-color-primary);
  text-decoration: none;
  border-bottom: 1px solid transparent;
  transition: border-color 0.2s;
}

.knowledge-content :deep(a:hover) {
  border-bottom-color: var(--el-color-primary);
}

/* ========== 强调 ========== */
.knowledge-content :deep(strong) {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

/* ========== 图片 ========== */
.knowledge-content :deep(img) {
  max-width: 100%;
  border-radius: 6px;
  margin: 8px 0;
}

/* ========== 回到顶部 ========== */
.back-top {
  position: fixed;
  right: 24px;
  bottom: 24px;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--el-bg-color-overlay);
  border: 1px solid var(--el-border-color-light);
  box-shadow: var(--el-box-shadow-light);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--el-text-color-regular);
  transition: all 0.2s;
  z-index: 20;
}

.back-top:hover {
  color: var(--el-color-primary);
  border-color: var(--el-color-primary);
  box-shadow: var(--el-box-shadow);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.3s;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
