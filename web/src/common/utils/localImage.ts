/**
 * 本地背景图存取工具：IndexedDB 存储 + 选图压缩。
 * 背景图属于"本机个性化"，刻意不走后端：各端（桌面壳/各浏览器）IndexedDB 独立互不共享；
 * IndexedDB 同源即可访问，登录页等匿名页面无需登录也能读取。
 */

const DB_NAME = 'convenient-system-local'
const STORE_NAME = 'images'

/** 背景图存储键：登录页背景 / 锁屏背景 */
export type LocalImageKey = 'loginBg' | 'lockBg'

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE_NAME)) {
        req.result.createObjectStore(STORE_NAME)
      }
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error ?? new Error('IndexedDB 打开失败'))
  })
}

/** 保存图片（Blob）到本地库 */
export async function saveLocalImage(key: LocalImageKey, blob: Blob): Promise<void> {
  const db = await openDb()
  try {
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readwrite')
      tx.objectStore(STORE_NAME).put(blob, key)
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error ?? new Error('IndexedDB 写入失败'))
    })
  } finally {
    db.close()
  }
}

/** 读取本地图片，未设置时返回 null */
export async function loadLocalImage(key: LocalImageKey): Promise<Blob | null> {
  const db = await openDb()
  try {
    return await new Promise<Blob | null>((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readonly')
      const req = tx.objectStore(STORE_NAME).get(key)
      req.onsuccess = () => resolve((req.result as Blob) ?? null)
      req.onerror = () => reject(req.error ?? new Error('IndexedDB 读取失败'))
    })
  } finally {
    db.close()
  }
}

/** 删除本地图片（恢复默认背景） */
export async function removeLocalImage(key: LocalImageKey): Promise<void> {
  const db = await openDb()
  try {
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readwrite')
      tx.objectStore(STORE_NAME).delete(key)
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error ?? new Error('IndexedDB 删除失败'))
    })
  } finally {
    db.close()
  }
}

/**
 * 选择文件 → 压缩为 JPEG：最长边 1920、质量 0.85。
 * 背景图不需要原图分辨率，压缩后通常几百 KB，IndexedDB 存储与桌面壳消息传输都轻松。
 * 返回压缩后的 Blob（存 IndexedDB）与 dataURL（发桌面壳 base64 落盘）。
 */
export async function compressImageFile(file: File): Promise<{ blob: Blob; dataUrl: string }> {
  // createImageBitmap 默认按 EXIF 方向解码，手机竖拍照片不会躺倒
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, 1920 / Math.max(bitmap.width, bitmap.height))
  const w = Math.max(1, Math.round(bitmap.width * scale))
  const h = Math.max(1, Math.round(bitmap.height * scale))
  const canvas = document.createElement('canvas')
  canvas.width = w
  canvas.height = h
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('画布创建失败')
  ctx.drawImage(bitmap, 0, 0, w, h)
  bitmap.close()
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.85))
  if (!blob) throw new Error('图片压缩失败')
  // 同一压缩结果转 dataURL（FileReader 读取，避免 canvas 二次编码）
  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(new Error('图片读取失败'))
    reader.readAsDataURL(blob)
  })
  return { blob, dataUrl }
}
