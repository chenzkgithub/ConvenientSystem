import { httpGet, httpPost, httpDelete } from '@/api/request'

/** Web 版本包 DTO */
export interface WebPackageDto {
  id: number
  version: string
  fileSize: number
  description?: string | null
  isActive: boolean
  createTime: string
}

/** 桌面安装包 DTO */
export interface DesktopPackageDto {
  id: number
  version: string
  fileSize: number
  description?: string | null
  isActive: boolean
  createdByName?: string | null
  createTime: string
}

// ========== Web 前端版本包 ==========

/** 全部 Web 版本包列表 */
export function listPackages() {
  return httpGet<WebPackageDto[]>('/api/Common/WebPackage/GetList')
}

/** 上传 Web 版本包 zip（自动激活为新版本） */
export function uploadPackage(version: string, file: File, description?: string) {
  const form = new FormData()
  form.append('version', version)
  form.append('file', file)
  if (description) form.append('description', description)
  // 文件较大，给 5 分钟超时；不手动设置 Content-Type，让浏览器自动添加 boundary
  return httpPost<WebPackageDto>('/api/Common/WebPackage/Upload', form, undefined, 300_000)
}

/** 激活指定 Web 版本 */
export function activatePackage(id: number) {
  return httpPost<void>('/api/Common/WebPackage/Activate', { id })
}

/** 停用指定 Web 版本（取消激活状态） */
export function deactivatePackage(id: number) {
  return httpPost<void>('/api/Common/WebPackage/Deactivate', { id })
}

/** 删除 Web 版本包 */
export function deletePackage(id: number) {
  return httpDelete<void>(`/api/Common/WebPackage/Delete?id=${id}`)
}

/** 修改 Web 版本号和更新说明 */
export function updatePackage(id: number, version: string, description?: string) {
  return httpPost<void>('/api/Common/WebPackage/Update', { id, version, description })
}

// ========== 桌面安装包 ==========

/** 全部桌面安装包列表 */
export function listDesktopPackages() {
  return httpGet<DesktopPackageDto[]>('/api/Common/DesktopUpdate/List')
}

/** 上传桌面安装包 exe（自动激活为新版本） */
export function uploadDesktopPackage(version: string, file: File, description?: string) {
  const form = new FormData()
  form.append('version', version)
  form.append('file', file)
  if (description) form.append('description', description)
  // 安装包可能较大，给 10 分钟超时
  return httpPost<DesktopPackageDto>('/api/Common/DesktopUpdate/Upload', form, undefined, 600_000)
}

/** 激活指定桌面安装包 */
export function activateDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Activate', { id })
}

/** 停用指定桌面安装包（取消激活状态） */
export function deactivateDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Deactivate', { id })
}

/** 删除桌面安装包 */
export function deleteDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Delete', { id })
}

/** 修改桌面安装包版本号和更新说明 */
export function updateDesktopPackage(id: number, version: string, description?: string) {
  return httpPost<void>('/api/Common/DesktopUpdate/Update', { id, version, description })
}
