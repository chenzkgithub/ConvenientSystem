import { ref } from 'vue'

/**
 * 本地已安装的前端版本信息（全局单例）。
 * 从 wwwroot/version.json 读取本地版本指纹，页面侧栏/登录页展示的是
 * 用户实际运行的前端版本，而非服务器激活版本。
 * version.json 由 WebUpdateService.DownloadAndExtractAsync 在下载 Web 包后写入。
 */

interface LocalVersionInfo {
  version: string
}

const data = ref<LocalVersionInfo | null>(null)
let pending: Promise<LocalVersionInfo | null> | null = null

// 注意：内部函数不能命名为 fetch，否则会遮蔽全局 fetch 造成递归调用自身。
async function load(): Promise<LocalVersionInfo | null> {
  if (data.value) return data.value
  if (pending) return pending

  pending = (async () => {
    try {
      const res = await window.fetch('/version.json', { cache: 'no-store' })
      if (res.ok) {
        const json = await res.json()
        if (json?.version) {
          data.value = { version: String(json.version) }
        }
      }
    } catch {
      // version.json 不存在时（安装包未含版本指纹）静默跳过
    } finally {
      pending = null
    }
    return data.value
  })()

  return pending
}

/** 获取本地已安装的前端版本信息（带缓存，多次调用只请求一次） */
export function useAppVersion() {
  return { data, fetch: load }
}
