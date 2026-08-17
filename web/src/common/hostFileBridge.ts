/**
 * C# 宿主文件操作桥接模块。
 *
 * 仅在 WebView2 环境下可用（window.chrome.webview 存在时）。
 * 浏览器中所有函数返回 null / false，组件自动回退到 File System Access API。
 *
 * 用法：
 *   initHostBridge((msg) => ElMessage.error(msg))   // onMounted
 *   disposeHostBridge()                               // onBeforeUnmount
 *   if (isHostAvailable()) { ... }                   // 判断是否 WebView2
 *   const r = await hostOpenFile()                    // 打开文件
 *   const r = await hostSaveFile(path, content)       // 保存到已有路径
 *   const r = await hostSaveFileAs(name, content)    // 另存为
 *   hostOpenLocation(path)                           // 资源管理器选中文件
 *   hostOpenRecycleBin()                             // 打开系统回收站
 */

// ---------- 类型 ----------

export interface HostFileOpened { path: string; fileName: string; content: string }
export interface HostFileSaved { path: string; fileName: string }

interface WebView2Host {
  postMessage: (message: unknown) => void
  addEventListener?: (type: string, listener: (ev: { data?: unknown }) => void) => void
  removeEventListener?: (type: string, listener: (ev: { data?: unknown }) => void) => void
}

// ---------- 内部状态 ----------

let _host: WebView2Host | undefined | null = null
let _listenerBound = false
let _errorCallback: ((msg: string) => void) | null = null
let _openResolver: ((data: HostFileOpened | null) => void) | null = null
let _saveResolver: ((data: HostFileSaved | null) => void) | null = null

function getHost(): WebView2Host | undefined {
  if (_host === null) {
    _host = (window as unknown as { chrome?: { webview?: WebView2Host } }).chrome?.webview ?? undefined
  }
  return _host
}

// ---------- 消息分发 ----------

function onMessage(ev: { data?: unknown }) {
  const d = ev?.data as Record<string, unknown> | undefined
  if (!d) return
  switch (d.type) {
    case 'file:opened':
      if (_openResolver) {
        _openResolver({
          path: (d.path as string) || '',
          fileName: (d.fileName as string) || '',
          content: (d.content as string) || '',
        })
        _openResolver = null
      }
      break
    case 'file:saved':
      if (_saveResolver) {
        _saveResolver({
          path: (d.path as string) || '',
          fileName: (d.fileName as string) || '',
        })
        _saveResolver = null
      }
      break
    case 'file:cancelled':
      // 用户取消对话框，静默返回 null
      if (d.action === 'open' && _openResolver) { _openResolver(null); _openResolver = null }
      else if (d.action === 'saveAs' && _saveResolver) { _saveResolver(null); _saveResolver = null }
      break
    case 'file:error':
      _errorCallback?.(`文件操作失败：${(d.message as string) || '未知错误'}`)
      if (_openResolver) { _openResolver(null); _openResolver = null }
      if (_saveResolver) { _saveResolver(null); _saveResolver = null }
      break
  }
}

// ---------- 公共 API ----------

/** 检查 C# 宿主是否可用 */
export function isHostAvailable(): boolean {
  return !!getHost()
}

/** 初始化宿主桥接（在组件 onMounted 中调用） */
export function initHostBridge(onError?: (msg: string) => void) {
  _errorCallback = onError ?? null
  const host = getHost()
  if (host?.addEventListener && !_listenerBound) {
    host.addEventListener('message', onMessage)
    _listenerBound = true
  }
}

/** 清理宿主桥接（在组件 onBeforeUnmount 中调用） */
export function disposeHostBridge() {
  const host = getHost()
  if (host?.removeEventListener && _listenerBound) {
    host.removeEventListener('message', onMessage)
    _listenerBound = false
  }
  _errorCallback = null
}

/** 通过 C# 宿主打开文件。返回 null 表示取消/失败/超时（错误已通过 onError 回调上报）。 */
export function hostOpenFile(timeoutMs = 15000): Promise<HostFileOpened | null> {
  const host = getHost()
  if (!host) return Promise.resolve(null)
  let timer: ReturnType<typeof setTimeout>
  return Promise.race([
    new Promise<HostFileOpened | null>((resolve) => {
      _openResolver = resolve
      host.postMessage({ type: 'file:open' })
    }),
    new Promise<null>((resolve) => {
      timer = setTimeout(() => {
        _errorCallback?.('打开文件超时')
        if (_openResolver) { _openResolver(null); _openResolver = null }
        resolve(null)
      }, timeoutMs)
    }),
  ]).then((result) => {
    clearTimeout(timer!)
    return result
  })
}

/** 通过 C# 宿主保存到已有路径。返回 null 表示失败/超时。 */
export function hostSaveFile(path: string, content: string, timeoutMs = 10000): Promise<HostFileSaved | null> {
  const host = getHost()
  if (!host) return Promise.resolve(null)
  let timer: ReturnType<typeof setTimeout>
  return Promise.race([
    new Promise<HostFileSaved | null>((resolve) => {
      _saveResolver = resolve
      host.postMessage({ type: 'file:save', path, content })
    }),
    new Promise<null>((resolve) => {
      timer = setTimeout(() => {
        _errorCallback?.('保存超时')
        if (_saveResolver) { _saveResolver(null); _saveResolver = null }
        resolve(null)
      }, timeoutMs)
    }),
  ]).then((result) => {
    clearTimeout(timer!)
    return result
  })
}

/** 通过 C# 宿主另存为。返回 null 表示取消/失败/超时（取消时静默，错误已通过 onError 上报）。 */
export function hostSaveFileAs(fileName: string, content: string, timeoutMs = 60000): Promise<HostFileSaved | null> {
  const host = getHost()
  if (!host) return Promise.resolve(null)
  let timer: ReturnType<typeof setTimeout>
  return Promise.race([
    new Promise<HostFileSaved | null>((resolve) => {
      _saveResolver = resolve
      host.postMessage({ type: 'file:saveAs', fileName, content })
    }),
    new Promise<null>((resolve) => {
      timer = setTimeout(() => {
        _errorCallback?.('保存超时')
        if (_saveResolver) { _saveResolver(null); _saveResolver = null }
        resolve(null)
      }, timeoutMs)
    }),
  ]).then((result) => {
    clearTimeout(timer!)
    return result
  })
}

/** 通过 C# 宿主在资源管理器中打开文件所在位置 */
export function hostOpenLocation(path: string): void {
  getHost()?.postMessage({ type: 'file:openExplorer', path })
}

/** 通过 C# 宿主打开系统回收站 */
export function hostOpenRecycleBin(): void {
  getHost()?.postMessage({ type: 'file:openRecycleBin' })
}
