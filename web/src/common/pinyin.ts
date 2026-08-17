/**
 * 拼音匹配：让中文内容能用拼音首字母缩写或全拼搜索，例如「菜单管理」可用 cdgl、caidan 搜到。
 * 目前用于侧栏菜单搜索（common/layout/MainLayout.vue），其它需要中文模糊搜索的地方可直接复用。
 */
import { pinyin } from 'pinyin-pro'

interface PinyinForms {
  /** 首字母缩写串，如「菜单管理」→ cdgl */
  initials: string
  /** 无声调全拼串，如「菜单管理」→ caidanguanli */
  full: string
}

/**
 * 转换结果缓存。菜单标题固定且数量有限，而搜索框每敲一个字符都要把全部标题重算一遍，
 * 缓存后只在首次输入时付一次转换成本。
 */
const cache = new Map<string, PinyinForms>()

function forms(text: string): PinyinForms {
  let hit = cache.get(text)
  if (!hit) {
    // separator: '' 让拼音之间不留空格，缩写/全拼才能被连续子串命中；
    // nonZh: 'consecutive' 保留英文与数字原样（如「Sms短信」→ smsdx）；
    // v: true 把 ü 换成 v，「女」按 nv 匹配，符合键盘输入习惯。
    const options = { toneType: 'none', separator: '', nonZh: 'consecutive', v: true } as const
    hit = {
      initials: pinyin(text, { ...options, pattern: 'first' }).toLowerCase(),
      full: pinyin(text, options).toLowerCase(),
    }
    cache.set(text, hit)
  }
  return hit
}

/**
 * 关键字在文本中的命中位置，未命中返回 -1。返回位置而不是布尔值，便于调用方把
 * 从头命中的结果排在前面。依次尝试：原文包含 → 拼音首字母包含 → 全拼包含。
 */
export function pinyinMatchIndex(text: string, keyword: string): number {
  const kw = keyword.trim().toLowerCase()
  if (!kw || !text) return -1
  const idx = text.toLowerCase().indexOf(kw)
  if (idx >= 0) return idx
  // 关键字里有非 ASCII 字符（通常就是中文）时不必再算拼音：拼音串里不会出现汉字，必然匹配不上
  if (/[^\x00-\x7f]/.test(kw)) return -1
  const { initials, full } = forms(text)
  const initialIdx = initials.indexOf(kw)
  if (initialIdx >= 0) return initialIdx
  return full.indexOf(kw)
}

/** 关键字是否命中文本（原文 / 拼音首字母 / 全拼任一命中） */
export function pinyinMatch(text: string, keyword: string): boolean {
  return pinyinMatchIndex(text, keyword) >= 0
}
