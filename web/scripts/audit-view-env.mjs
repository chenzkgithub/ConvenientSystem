#!/usr/bin/env node
/**
 * audit-view-env.mjs — 视图运行环境静态审计（桌面/Web 接口分离守护）
 *
 * 三条规则：
 *  a. VIEW_META 登记的组件路径必须真实存在于 src 下（元数据不失真）；
 *  b. import 了 API_SIDE==='local' 模块的视图，env 必须是 desktop
 *     （mixed/remote 模块不限；真源 = viewComponents.ts 的 VIEW_META，非 DB SysView.Env）；
 *  c. env 为 web/both 的视图不得直拼 '/api/local/' 字面量
 *     （本地通道只归桌面端视图使用；远程 /api/ 字面量属历史风格，不在本期治理范围）。
 *
 * 审计范围：src 下所有 views 目录中的 .vue（"视图"=页面级组件，与 VIEW_META 注册口径一致；
 * 通用组件如 UpdateBanner 自带宿主短路守卫，不在本审计范围）。
 * 豁免：确认无误的命中行行尾加注释 // audit-ignore。
 *
 * 退出码：0=通过；1=有错误（CI 失败）。
 */
import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs'
import { join, dirname, resolve, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const WEB_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const SRC_ROOT = join(WEB_ROOT, 'src')

/** 递归收集目录下指定后缀的文件（跳过 node_modules 与隐藏目录） */
function walk(dir, exts, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    const st = statSync(p)
    if (st.isDirectory()) {
      if (name === 'node_modules' || name.startsWith('.')) continue
      walk(p, exts, out)
    } else if (exts.some((e) => name.endsWith(e))) {
      out.push(p)
    }
  }
  return out
}

/** 与 viewComponents.ts 同规则的 key 归一（复制实现，避免引 TS 源） */
function normalizeKey(key) {
  if (key.startsWith('./views/')) return '/src/common/views/' + key.slice('./views/'.length)
  if (key.startsWith('../')) return '/src/' + key.slice('../'.length)
  return key
}

/** 从 viewComponents.ts 解析 VIEW_META（正则提取，注释行自然被跳过） */
function parseViewMeta() {
  const src = readFileSync(join(SRC_ROOT, 'common', 'viewComponents.ts'), 'utf8')
  const block = src.match(/const\s+VIEW_META[^{]*\{([\s\S]*?)\n\}/)?.[1] ?? ''
  const meta = {}
  const re = /'([^']+)'\s*:\s*\{\s*env\s*:\s*'(desktop|web|both)'\s*\}/g
  let m
  while ((m = re.exec(block))) meta[normalizeKey(m[1])] = m[2]
  return meta
}

/** 视图文件绝对路径 → /src/... 规范路径 */
function toSrcPath(file) {
  return '/' + relative(WEB_ROOT, file).split(sep).join('/')
}

/** 解析 import 说明符到实际文件（null=裸导入或解析失败，不参与审计） */
function resolveImport(spec, fromFile) {
  let base = null
  if (spec.startsWith('@/')) base = join(SRC_ROOT, spec.slice(2))
  else if (spec.startsWith('.')) base = resolve(dirname(fromFile), spec)
  if (!base) return null
  for (const c of [base, base + '.ts', base + '.d.ts', base + '.vue', join(base, 'index.ts')]) {
    if (existsSync(c)) return c
  }
  return null
}

/** 提取文件内全部 import 说明符 */
function parseImports(content) {
  const specs = []
  const re = /import\s+(?:type\s+)?(?:[^'"]*?\s+from\s+)?['"]([^'"]+)['"]/g
  let m
  while ((m = re.exec(content))) specs.push(m[1])
  return specs
}

const errors = []
const viewMeta = parseViewMeta()

// ── 规则 a：VIEW_META 登记的组件路径必须真实存在 ──
for (const [path] of Object.entries(viewMeta)) {
  const abs = join(WEB_ROOT, path.replace(/^\//, ''))
  if (!existsSync(abs)) errors.push(`[a] VIEW_META 登记的组件不存在：${path}`)
}

// ── 收集 api 模块的 API_SIDE（未标注默认 remote，与接口分离批次口径一致） ──
const apiSide = new Map() // 绝对路径 → side
const apiFiles = walk(SRC_ROOT, ['.ts']).filter((f) => /[\\/]api[\\/]/.test(f))
for (const f of apiFiles) {
  const m = readFileSync(f, 'utf8').match(/export\s+const\s+API_SIDE\s*=\s*'(\w+)'/)
  apiSide.set(f, m?.[1] ?? 'remote')
}

// ── 规则 b/c：逐视图审计 ──
const viewFiles = walk(SRC_ROOT, ['.vue']).filter((f) => /[\\/]views[\\/]/.test(f))
for (const file of viewFiles) {
  const srcPath = toSrcPath(file)
  const env = viewMeta[srcPath] ?? 'both'
  const content = readFileSync(file, 'utf8')

  // 规则 b：import 本地接口模块的视图必须登记为 desktop
  for (const spec of parseImports(content)) {
    const target = resolveImport(spec, file)
    if (!target) continue
    if (apiSide.get(target) === 'local' && env !== 'desktop') {
      errors.push(`[b] ${srcPath}（env=${env}）import 了本地接口模块 ${spec}，应在 VIEW_META 登记为 desktop`)
    }
  }

  // 规则 c：web/both 视图不得直拼 /api/local/ 字面量
  if (env !== 'desktop') {
    content.split(/\r?\n/).forEach((line, i) => {
      if (/['"`]\/api\/local\//.test(line) && !line.includes('audit-ignore')) {
        errors.push(`[c] ${srcPath}:${i + 1}（env=${env}）直拼 /api/local/ 字面量：${line.trim()}`)
      }
    })
  }
}

// ── 汇总 ──
if (errors.length > 0) {
  console.error(`✗ 视图环境审计失败，共 ${errors.length} 处：`)
  for (const e of errors) console.error('  ' + e)
  console.error('（确认无误可命中行加 // audit-ignore 豁免）')
  process.exit(1)
}
console.log(`✓ 视图环境审计通过（VIEW_META ${Object.keys(viewMeta).length} 条，审计视图 ${viewFiles.length} 个，api 模块 ${apiFiles.length} 个）`)
