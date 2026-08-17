// 全屏相关工具：浏览器 Fullscreen API 全屏时只渲染全屏元素及其子树，
// 挂载在 document.body 上的弹层（ElMessage 等）会被全屏层遮挡，
// 需通过 appendTo 挂到全屏元素内部才能可见。

/** 当前全屏元素（HTMLElement 且处于全屏状态时返回），用作 ElMessage.appendTo / ElLoading target */
export function fullscreenElement(): HTMLElement | undefined {
  const fs = document.fullscreenElement
  return fs instanceof HTMLElement ? fs : undefined
}

/** 全屏元素作为遮罩/消息容器时确保其为定位元素（ElLoading 遮罩依赖 position 定位） */
export function ensurePositioned(el: HTMLElement): void {
  if (getComputedStyle(el).position === 'static') el.style.position = 'relative'
}
