import { httpGet, httpPost, httpDelete } from '@/api/request'

/** Web 鐗堟湰鍖?DTO */
export interface WebPackageDto {
  id: number
  version: string
  fileSize: number
  description?: string | null
  isActive: boolean
  createTime: string
}

/** 妗岄潰瀹夎鍖?DTO */
export interface DesktopPackageDto {
  id: number
  version: string
  fileSize: number
  description?: string | null
  isActive: boolean
  createdByName?: string | null
  createTime: string
}

// ========== Web 鍓嶇鐗堟湰鍖?==========

/** 鍏ㄩ儴 Web 鐗堟湰鍖呭垪琛?*/
export function listPackages() {
  return httpGet<WebPackageDto[]>('/api/Common/WebPackage/GetList')
}

/** 涓婁紶 Web 鐗堟湰鍖?zip锛堣嚜鍔ㄦ縺娲讳负鏂扮増鏈級 */
export function uploadPackage(version: string, file: File, description?: string, onProgress?: (pct: number) => void) {
  const form = new FormData()
  form.append('version', version)
  form.append('file', file)
  if (description) form.append('description', description)
  // 鏂囦欢杈冨ぇ锛岀粰 5 鍒嗛挓瓒呮椂锛涗笉鎵嬪姩璁剧疆 Content-Type锛岃娴忚鍣ㄨ嚜鍔ㄦ坊鍔?boundary
  return httpPost<WebPackageDto>('/api/Common/WebPackage/Upload', form, undefined, 300_000, { noLoading: true, onUploadProgress: onProgress })
}

/** 婵€娲绘寚瀹?Web 鐗堟湰 */
export function activatePackage(id: number) {
  return httpPost<void>('/api/Common/WebPackage/Activate', { id })
}

/** 鍋滅敤鎸囧畾 Web 鐗堟湰锛堝彇娑堟縺娲荤姸鎬侊級 */
export function deactivatePackage(id: number) {
  return httpPost<void>('/api/Common/WebPackage/Deactivate', { id })
}

/** 鍒犻櫎 Web 鐗堟湰鍖?*/
export function deletePackage(id: number) {
  return httpDelete<void>(`/api/Common/WebPackage/Delete?id=${id}`)
}

/** 淇敼 Web 鐗堟湰鍙峰拰鏇存柊璇存槑 */
export function updatePackage(id: number, version: string, description?: string) {
  return httpPost<void>('/api/Common/WebPackage/Update', { id, version, description })
}

// ========== 妗岄潰瀹夎鍖?==========

/** 鍏ㄩ儴妗岄潰瀹夎鍖呭垪琛?*/
export function listDesktopPackages() {
  return httpGet<DesktopPackageDto[]>('/api/Common/DesktopUpdate/List')
}

/** 涓婁紶妗岄潰瀹夎鍖?exe锛堣嚜鍔ㄦ縺娲讳负鏂扮増鏈級 */
export function uploadDesktopPackage(version: string, file: File, description?: string, onProgress?: (pct: number) => void) {
  const form = new FormData()
  form.append('version', version)
  form.append('file', file)
  if (description) form.append('description', description)
  // 瀹夎鍖呭彲鑳借緝澶э紝缁?10 鍒嗛挓瓒呮椂
  return httpPost<DesktopPackageDto>('/api/Common/DesktopUpdate/Upload', form, undefined, 600_000, { noLoading: true, onUploadProgress: onProgress })
}

/** 婵€娲绘寚瀹氭闈㈠畨瑁呭寘 */
export function activateDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Activate', { id })
}

/** 鍋滅敤鎸囧畾妗岄潰瀹夎鍖咃紙鍙栨秷婵€娲荤姸鎬侊級 */
export function deactivateDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Deactivate', { id })
}

/** 鍒犻櫎妗岄潰瀹夎鍖?*/
export function deleteDesktopPackage(id: number) {
  return httpPost<void>('/api/Common/DesktopUpdate/Delete', { id })
}

/** 淇敼妗岄潰瀹夎鍖呯増鏈彿鍜屾洿鏂拌鏄?*/
export function updateDesktopPackage(id: number, version: string, description?: string) {
  return httpPost<void>('/api/Common/DesktopUpdate/Update', { id, version, description })
}
