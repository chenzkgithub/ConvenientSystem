/**
 * 全局自动悬浮提示（解析规则）：让系统内任何界面——包括以后新开发的页面——不写任何代码，
 * 就自动获得与 CommonTooltip 完全一致的统一浮层（浅蓝底 + 箭头 + 右上角复制按钮 +
 * 0.5s 延迟显示 / 5s 自动消失 / 可移入复制）。浮层载体见 components/GlobalAutoTip.vue。
 *
 * 两个触发来源（按优先级）：
 * 1. 元素带原生 title：读出文本后把 title 摘掉，否则浏览器自带的灰色提示会和统一浮层
 *    同时冒出来；文本缓存到 data-cs-tip-text，供该元素再次悬浮时使用。
 *    这样页面里保留 title 写法即可，无需逐个改成组件。
 * 2. 文本被省略号截断：只认单行 text-overflow: ellipsis 与多行 -webkit-line-clamp 两种，
 *    自动把完整文本当作提示内容。不能顺带认普通的 overflow: hidden——那样任何为了裁剪
 *    布局而写 overflow: hidden 的容器都会被判成“内容显示不全”，把整块文字弹出来。
 *
 * 跳过的场景（用 closest 判定，含祖先）：
 * - .el-popper：浮层内部（提示正文、复制按钮都在里面），避免提示套提示；
 * - .el-tooltip__trigger（表格单元格外）：Element Plus 给所有 ElTooltip 触发元素加的类名
 *   （见 element-plus/es/components/tooltip/src/trigger.vue 的 class: ns.e('trigger')）。
 *   页面里用 CommonTooltip 写的提示，以及 el-select / el-dropdown / el-popover 等内部
 *   复用 ElTooltip 的组件，都由它们自己接管，这里不重复弹；
 * - [data-cs-tip-off]：需要显式关闭自动提示时，给元素或其容器加这个属性。
 *
 * 表格单元格内的特殊处理：
 * - EP 的 showOverflowTooltip 在 cell 级包裹 .el-tooltip__trigger，但它只显示 cell 文本，
 *   不处理子元素的 title。因此单元格内仍需处理子元素 title（否则走浏览器原生灰色提示）。
 * - 但跳过截断检测：cell 级溢出由 EP showOverflowTooltip 处理，这里再检测会两个浮层同时弹。
 * - 遇到 .el-tooltip__trigger（EP 的 cell wrapper）时停止向上查找，不越过 cell 边界。
 */

/** 命中的提示目标 */
export interface TipTarget {
  /** 作为浮层锚点的元素 */
  el: HTMLElement
  /** 提示内容（纯文本） */
  content: string
}

/** 原生 title 摘除后的文本缓存属性 */
const TEXT_ATTR = 'data-cs-tip-text'

/** 显式关闭自动提示的属性（作用于元素自身及其子树） */
const OFF_ATTR = 'data-cs-tip-off'

/** 完全跳过的子树（title 和截断都不处理） */
const SKIP_SELECTOR = `.el-popper, [${OFF_ATTR}]`

/** 表格单元格选择器：其内仍处理 title，但跳过截断检测 */
const TABLE_CELL_SELECTOR = `.el-table__cell`

/** title 是无障碍属性、不作为悬浮提示处理的标签 */
const KEEP_TITLE_TAGS = new Set(['IFRAME'])

/** 不做截断检测的标签：表单控件内容超长时 scrollWidth 天然大于 clientWidth，会在输入时不停弹提示 */
const SKIP_CLAMP_TAGS = new Set(['INPUT', 'TEXTAREA', 'SELECT', 'IFRAME'])

/** 向上查找的最大层数：够覆盖“文本节点 → 截断的那层元素”，又不至于把整块区域当成提示 */
const MAX_WALK = 6

/**
 * 取元素的显式提示文本，并把原生 title 摘掉换成自己的缓存属性。
 * Vue 更新 :title 绑定时会重新写上 title，下次悬浮读到的仍是最新值。
 */
function takeTitle(el: HTMLElement): string {
  if (KEEP_TITLE_TAGS.has(el.tagName)) return ''
  const raw = el.getAttribute('title')
  if (raw !== null) {
    el.removeAttribute('title')
    if (raw.trim()) el.setAttribute(TEXT_ATTR, raw)
    else el.removeAttribute(TEXT_ATTR)
  }
  return (el.getAttribute(TEXT_ATTR) ?? '').trim()
}

/** 元素文本是否被省略号截断；是则返回完整文本 */
function clampedText(el: HTMLElement): string {
  if (SKIP_CLAMP_TAGS.has(el.tagName)) return ''
  const cs = getComputedStyle(el)
  const singleLine = cs.textOverflow === 'ellipsis' && (cs.whiteSpace === 'nowrap' || cs.whiteSpace === 'pre')
  const lineClamp = cs.getPropertyValue('-webkit-line-clamp')
  const multiLine = !!lineClamp && lineClamp !== 'none'
  // 留 1px 容差：缩放比例下 scrollWidth/clientWidth 会有亚像素取整误差
  if (singleLine) {
    if (el.scrollWidth <= el.clientWidth + 1) return ''
  } else if (multiLine) {
    if (el.scrollHeight <= el.clientHeight + 1) return ''
  } else {
    return ''
  }
  return (el.textContent ?? '').trim()
}

/**
 * 由鼠标事件目标解析出该显示哪条提示。
 * 从事件目标逐层向上找：先看显式 title，再看是否被省略号截断。
 */
export function resolveTipTarget(target: EventTarget | null): TipTarget | null {
  if (!(target instanceof HTMLElement)) return null
  if (target.closest(SKIP_SELECTOR)) return null

  // 表格单元格内：EP 的 showOverflowTooltip 在 cell 级包裹 .el-tooltip__trigger，
  // 但它只显示 cell 文本、不处理子元素 title，因此这里仍需处理子元素 title。
  const inTableCell = !!target.closest(TABLE_CELL_SELECTOR)
  // 表格单元格外：跳过 .el-tooltip__trigger（CommonTooltip / el-select 等自己接管）
  if (!inTableCell && target.closest('.el-tooltip__trigger')) return null

  let el: HTMLElement | null = target
  for (let i = 0; i < MAX_WALK && el; i++, el = el.parentElement) {
    // 表格单元格内遇到 .el-tooltip__trigger（EP 的 cell 级 wrapper）时停止向上：
    // EP 的 showOverflowTooltip 只显示 cell 文本，不处理子元素 title
    if (inTableCell && el.classList.contains('el-tooltip__trigger')) break
    const explicit = takeTitle(el)
    if (explicit) return { el, content: explicit }
    // 表格单元格内跳过截断检测（cell 级溢出由 EP showOverflowTooltip 处理）
    if (!inTableCell) {
      const clamped = clampedText(el)
      if (clamped) return { el, content: clamped }
    }
  }
  return null
}
