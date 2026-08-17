/**
 * 悬浮提示复制按钮增强：给系统内所有统一样式的提示浮层右上角补一个复制按钮。
 * 覆盖两类浮层，二者外观与行为完全一致：
 * - 表格单元格的溢出提示：由 Element Plus 表格内部创建（全局配置见 App.vue 的
 *   el-config-provider :table），既不在页面模板里也没有插槽，只能像 dialogFlex 那样
 *   用 MutationObserver 事后注入；
 * - 页面里用 CommonTooltip 写的提示：组件已带上同样的 popperClass，走同一套注入逻辑。
 * 判定依据是浮层的 cs-tip-copyable 类名（纯操作说明类提示不带该类，不会被注入）。
 * 提示浮层需要 enterable + hideAfter 才能让鼠标从触发元素移进浮层点按钮，
 * 这两项分别由 CommonTooltip 与 App.vue 的表格全局配置提供。
 * 浅蓝底色、内容层高度上限与按钮样式见 styles/main.css。
 */
import { ElMessage } from 'element-plus'

/** 需要注入复制按钮的浮层类名（CommonTooltip 与表格全局配置共用） */
const TIP_CLASS = 'cs-tip-copyable'

/** 复制按钮类名（样式在 main.css） */
const COPY_CLASS = 'cs-tip-copy'

/** 已注入过按钮的浮层标记，避免重复添加 */
const MARK_ATTR = 'data-cs-tip'

/** 复制图标（等价于 @element-plus/icons-vue 的 DocumentCopy，此处为原生 DOM 故内联 svg） */
const COPY_ICON =
  '<svg viewBox="0 0 1024 1024" aria-hidden="true"><path fill="currentColor" d="M768 832a128 128 0 0 1-128 128H192A128 128 0 0 1 64 832V384a128 128 0 0 1 128-128v64a64 64 0 0 0-64 64v448a64 64 0 0 0 64 64h448a64 64 0 0 0 64-64h64z"/><path fill="currentColor" d="M384 128a64 64 0 0 0-64 64v448a64 64 0 0 0 64 64h448a64 64 0 0 0 64-64V192a64 64 0 0 0-64-64H384zm0-64h448a128 128 0 0 1 128 128v448a128 128 0 0 1-128 128H384a128 128 0 0 1-128-128V192A128 128 0 0 1 384 64z"/></svg>'

let scheduled = false

/** rAF 节流：浮层创建引起的 DOM 变化集中处理一次 */
function schedule() {
  if (scheduled) return
  scheduled = true
  requestAnimationFrame(() => {
    scheduled = false
    scan()
  })
}

function scan() {
  document
    .querySelectorAll<HTMLElement>(`.${TIP_CLASS}:not([${MARK_ATTR}])`)
    .forEach(inject)
}

/**
 * 取浮层正文：跳过箭头与复制按钮本身。
 * 每次点击时实时读取，浮层被复用（同一元素再次悬浮）时也不会复制到过期内容。
 */
function readTipText(tip: HTMLElement): string {
  let text = ''
  tip.childNodes.forEach((node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      text += node.nodeValue ?? ''
      return
    }
    const el = node as HTMLElement
    if (el.classList?.contains('el-popper__arrow') || el.classList?.contains(COPY_CLASS)) return
    text += el.textContent ?? ''
  })
  return text.trim()
}

/** 兜底复制：非安全上下文（http 页面）下 navigator.clipboard 不可用 */
function copyByTextarea(text: string) {
  const ta = document.createElement('textarea')
  ta.value = text
  ta.style.position = 'fixed'
  ta.style.opacity = '0'
  document.body.appendChild(ta)
  ta.select()
  document.execCommand('copy')
  document.body.removeChild(ta)
}

async function copyText(text: string) {
  if (!text) return
  try {
    if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(text)
    else copyByTextarea(text)
    ElMessage.success({ message: '已复制', grouping: true, duration: 1500 })
  } catch {
    ElMessage.error({ message: '复制失败，请手动选中内容复制', grouping: true })
  }
}

function inject(tip: HTMLElement) {
  tip.setAttribute(MARK_ATTR, '1')
  const btn = document.createElement('button')
  btn.type = 'button'
  btn.className = COPY_CLASS
  btn.title = '复制内容'
  btn.innerHTML = COPY_ICON
  btn.addEventListener('click', (e) => {
    // 阻止冒泡：表格提示浮层挂在表格内部，点击不应触发行点击等事件
    e.stopPropagation()
    void copyText(readTipText(tip))
  })
  // 按钮必须挂在浮层本体上，不能放进内容层（.cs-tip-body / 表格提示的 span）：
  // 提示文本变化时 Vue 会整体重设内容层的 textContent，会把按钮一起清掉。
  tip.insertBefore(btn, tip.firstChild)
}

/** 安装提示浮层复制按钮增强（main.ts 中调用一次） */
export function installTipCopy() {
  new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true })
}
