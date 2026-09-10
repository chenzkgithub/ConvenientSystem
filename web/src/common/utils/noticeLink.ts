/** 通知正文分段：文本段与可点击链接段（发布包通知的下载链接等） */
export interface NoticeContentSegment {
  /** 文本段为原文片段；链接段为完整 URL */
  text: string
  /** 存在时该段为可点击链接（相对路径如 /api/... 或完整 http(s):// 地址） */
  url?: string
}

/** 通知正文中的链接形态：以 /api/ 开头的相对路径或完整 http(s) 地址 */
const URL_PATTERN = /(https?:\/\/[^\s]+|\/api\/[^\s]+)/g

/**
 * 把通知正文拆成 文本/链接 交替的分段序列，供 NoticeAlert 卡片与 NoticeBell 弹层渲染。
 * 无链接时退化为单一文本段（渲染与纯文本一致，不影响既有通知）。
 * 相对链接由浏览器按当前源解析：浏览器直开走站点同域，桌面端 WebView2 走本机反代，两端通吃。
 */
export function parseNoticeContent(content: string): NoticeContentSegment[] {
  if (!content) return []
  const segments: NoticeContentSegment[] = []
  let last = 0
  for (const m of content.matchAll(URL_PATTERN)) {
    const idx = m.index ?? 0
    if (idx > last) segments.push({ text: content.slice(last, idx) })
    segments.push({ text: m[0], url: m[0] })
    last = idx + m[0].length
  }
  if (last < content.length) segments.push({ text: content.slice(last) })
  return segments.length > 0 ? segments : [{ text: content }]
}
