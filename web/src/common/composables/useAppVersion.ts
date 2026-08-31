import { ref } from 'vue'
import { httpGet } from '@/api/request'

/**
 * 当前激活的前端版本包信息（全局单例）。
 * 多处（登录页、主框架、版本管理页）共用同一份数据，只发一次请求。
 */

interface ActiveVersionInfo {
  version: string
  description?: string | null
  fileSize?: number
  createTime?: string
}

const data = ref<ActiveVersionInfo | null>(null)
let pending: Promise<ActiveVersionInfo | null> | null = null

async function fetch() {
  if (data.value) return data.value
  if (pending) return pending

  pending = (async () => {
    try {
      const res = await httpGet<{
        hasVersion: boolean
        version: string
        description?: string | null
        fileSize?: number
        createTime?: string
      }>('/api/Common/WebPackage/GetActive')

      if (res?.hasVersion) {
        data.value = {
          version: res.version,
          description: res.description,
          fileSize: res.fileSize,
          createTime: res.createTime,
        }
      }
    } catch {
      // 接口不可用时静默，不影响页面正常渲染
    } finally {
      pending = null
    }
    return data.value
  })()

  return pending
}

/** 获取当前激活的前端版本信息（带缓存，多次调用只请求一次） */
export function useAppVersion() {
  return { data, fetch }
}
