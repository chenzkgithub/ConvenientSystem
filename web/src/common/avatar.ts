/**
 * 头像图片处理：把用户选择的图片压缩为可直接存库的 data URL。
 *
 * 头像随 SysUser 记录以 base64 存储（后端无静态上传目录，桌面壳与接口服务是两个
 * 独立发布产物），因此必须在前端压缩，否则原图 base64 会撑爆请求与数据库字段。
 */

/** 头像输出边长（正方形，中心裁剪） */
const AVATAR_SIZE = 200

/** 允许选择的原始图片体积上限（压缩前），超出直接拒绝以免解码大图卡住页面 */
export const AVATAR_SOURCE_MAX_BYTES = 5 * 1024 * 1024

/** 允许的图片类型 */
export function isImageFile(file: File) {
  return /^image\/(png|jpe?g|gif|bmp|webp)$/i.test(file.type)
}

/**
 * 读取图片文件并压缩为 200x200 的 JPEG data URL。
 * 采用中心裁剪保持不变形；JPEG 不支持透明，先填白底避免透明区域变黑。
 */
export function compressAvatar(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error('读取图片失败'))
    reader.onload = () => {
      const img = new Image()
      img.onerror = () => reject(new Error('图片解析失败，请更换图片'))
      img.onload = () => {
        try {
          const canvas = document.createElement('canvas')
          canvas.width = AVATAR_SIZE
          canvas.height = AVATAR_SIZE
          const ctx = canvas.getContext('2d')
          if (!ctx) return reject(new Error('当前环境不支持图片压缩'))
          ctx.fillStyle = '#ffffff'
          ctx.fillRect(0, 0, AVATAR_SIZE, AVATAR_SIZE)

          // 中心正方形裁剪源区域
          const side = Math.min(img.width, img.height)
          const sx = (img.width - side) / 2
          const sy = (img.height - side) / 2
          ctx.drawImage(img, sx, sy, side, side, 0, 0, AVATAR_SIZE, AVATAR_SIZE)

          resolve(canvas.toDataURL('image/jpeg', 0.85))
        } catch (e) {
          reject(e instanceof Error ? e : new Error(String(e)))
        }
      }
      img.src = String(reader.result || '')
    }
    reader.readAsDataURL(file)
  })
}
