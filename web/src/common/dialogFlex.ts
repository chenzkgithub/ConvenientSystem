/**
 * 全局弹窗拖动/拉伸增强。
 * 通过 MutationObserver 自动为所有 ElDialog（.el-dialog）附加，无需逐页面配置：
 * - 按住标题栏拖动窗口位置（自动避开关闭按钮等可交互元素）
 * - 拖动右下角手柄调整弹窗宽高（最小宽 300px、最小高 180px，内容超出时弹窗内部滚动）
 * - 带操作按钮（保存/清理等）或表单的弹窗点击遮罩不自动关闭，纯查看类弹窗保持点旁关闭
 * 配套布局样式见 styles/main.css（.el-dialog flex 列布局 + body 内部滚动 + 拉伸手柄样式）。
 */

/** 已增强的弹窗标记属性，避免重复绑定 */
const MARK_ATTR = 'data-cs-flex'

let scheduled = false

/** 扫描并为未增强的弹窗绑定拖动与拉伸 */
function scan() {
  document.querySelectorAll<HTMLElement>(`.el-dialog:not([${MARK_ATTR}])`).forEach(enhance)
}

/** rAF 节流：弹窗打开引起的 DOM 变化集中处理一次 */
function schedule() {
  if (scheduled) return
  scheduled = true
  requestAnimationFrame(() => {
    scheduled = false
    scan()
  })
}

function enhance(dialog: HTMLElement) {
  dialog.setAttribute(MARK_ATTR, '1')
  enableDrag(dialog)
  if (!dialog.classList.contains('is-fullscreen')) enableResize(dialog)
}

/** 标题栏按住拖动（transform 平移，不影响弹窗原有定位） */
function enableDrag(dialog: HTMLElement) {
  const header = dialog.querySelector<HTMLElement>('.el-dialog__header')
  if (!header) return
  header.style.cursor = 'move'
  header.addEventListener('mousedown', (e) => {
    // 关闭按钮及头部内的表单控件不触发拖动
    if ((e.target as HTMLElement).closest('.el-dialog__headerbtn, .el-button, .el-input, .el-select')) return
    e.preventDefault()
    const startX = e.clientX
    const startY = e.clientY
    const m = new DOMMatrix(getComputedStyle(dialog).transform)
    const baseX = m.m41
    const baseY = m.m42
    const move = (ev: MouseEvent) => {
      dialog.style.transform = `translate(${baseX + ev.clientX - startX}px, ${baseY + ev.clientY - startY}px)`
    }
    const up = () => {
      document.removeEventListener('mousemove', move)
      document.removeEventListener('mouseup', up)
      document.body.style.userSelect = ''
    }
    document.body.style.userSelect = 'none'
    document.addEventListener('mousemove', move)
    document.addEventListener('mouseup', up)
  })
}

/** 右下角手柄拖动改变弹窗宽高 */
function enableResize(dialog: HTMLElement) {
  const handle = document.createElement('div')
  handle.className = 'cs-dialog-resizer'
  handle.title = '拖动调整弹窗大小'
  dialog.appendChild(handle)
  handle.addEventListener('mousedown', (e) => {
    e.preventDefault()
    e.stopPropagation()
    const startX = e.clientX
    const startY = e.clientY
    const startW = dialog.offsetWidth
    const startH = dialog.offsetHeight
    const move = (ev: MouseEvent) => {
      dialog.style.width = `${Math.max(300, startW + ev.clientX - startX)}px`
      dialog.style.height = `${Math.max(180, startH + ev.clientY - startY)}px`
    }
    const up = () => {
      document.removeEventListener('mousemove', move)
      document.removeEventListener('mouseup', up)
      document.body.style.userSelect = ''
    }
    document.body.style.userSelect = 'none'
    document.addEventListener('mousemove', move)
    document.addEventListener('mouseup', up)
  })
}

/**
 * 判定是否为操作类弹窗：底部有彩色操作按钮（保存/清理/删除确认等）或内容包含表单。
 * 操作类弹窗点击遮罩不自动关闭，防止误点丢失已填写内容；纯查看类弹窗保持默认点旁关闭。
 */
function isActionDialog(dialog: HTMLElement): boolean {
  if (dialog.querySelector(
    '.el-dialog__footer .el-button--primary, .el-dialog__footer .el-button--success, '
    + '.el-dialog__footer .el-button--warning, .el-dialog__footer .el-button--danger',
  )) return true
  return dialog.querySelector('.el-dialog__body .el-form') != null
}

/**
 * 遮罩点击关闭拦截：EP 在 .el-overlay-dialog 的 click 冒泡阶段执行关闭，
 * 在 document 捕获阶段先行判定并 stopPropagation 即可阻止操作类弹窗被误关。
 */
function interceptMaskClose() {
  document.addEventListener('click', (e) => {
    const t = e.target as HTMLElement
    if (!t.classList?.contains('el-overlay-dialog')) return
    const dialog = t.querySelector<HTMLElement>('.el-dialog')
    if (dialog && isActionDialog(dialog)) e.stopPropagation()
  }, true)
}

/** 安装全局弹窗拖动/拉伸增强（main.ts 中调用一次） */
export function installDialogFlex() {
  scan()
  interceptMaskClose()
  new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true })
}
