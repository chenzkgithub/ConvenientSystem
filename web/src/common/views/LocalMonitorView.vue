<script setup lang="ts">
import { ref, shallowRef, computed, watch, onMounted, onBeforeUnmount, nextTick, h, type Component } from 'vue'
import { ElMessage, ElCheckbox, ElButton, ElIcon, TableV2SortOrder } from 'element-plus'
import { CopyDocument, Refresh, Search, Document, Picture, VideoCamera, Headset, Box, Setting, Cpu, Tickets, Delete } from '@element-plus/icons-vue'
import { httpGet, httpPost } from '@/api/request'
import { formatDate } from '@/common/formatDate'
import BaseChart from '@/common/components/BaseChart.vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { isHostAvailable, hostOpenLocation, hostOpenRecycleBin } from '@/common/hostFileBridge'
import { usePermission } from '@/common/composables/usePermission'
const { has } = usePermission()

// ── 类型 ──
interface HostMetrics {
  cpuPercent: number | null; cpuCores: number | null; memoryPercent: number | null
  memoryTotalGb: number | null; memoryUsedGb: number | null; osName: string | null
  uptimeHours: number | null; processCount: number | null
  netInKbps: number | null; netOutKbps: number | null
  diskReadMbPerSec: number | null; diskWriteMbPerSec: number | null
  disks: { drive: string; usedPercent: number; totalGb: number; freeGb: number }[]
  checkedAt: string
}

// ── 概览数据（页面打开自动加载，定时轮询刷新） ──
const overviewLoading = ref(false)
const metrics = ref<HostMetrics | null>(null)
let overviewFetching = false
async function loadOverview(silent = false) {
  if (overviewFetching) return
  overviewFetching = true
  if (!silent) overviewLoading.value = true
  try {
    metrics.value = await httpGet<HostMetrics>('/api/Common/Monitor/Overview', undefined, undefined, { silent })
  } catch { if (!silent) metrics.value = null }
  finally { overviewFetching = false; if (!silent) overviewLoading.value = false }
}
onMounted(() => loadOverview())
let overviewTimer: ReturnType<typeof setInterval> | null = null
onMounted(() => { overviewTimer = setInterval(() => { if (document.hidden) return; loadOverview(true) }, 30_000) })
onBeforeUnmount(() => { if (overviewTimer) { clearInterval(overviewTimer); overviewTimer = null } })

function percentColor(v: number): string { if (v >= 90) return '#f56c6c'; if (v >= 75) return '#e6a23c'; return '#67c23a' }
function uptimeText(h: number): string { return h < 48 ? `${h.toFixed(1)} 小时` : `${(h / 24).toFixed(1)} 天` }

// ── 设备规格与网络信息 ──
interface HostSystemInfo { deviceName: string; model: string; processor: string; ram: string; gpu: string; storage: string; deviceId: string; productId: string; systemType: string; penTouch: string; networkText: string }
const sysInfo = ref<HostSystemInfo | null>(null)
let sysInfoCached: HostSystemInfo | null = null
async function loadSystemInfo(force = false) {
  if (!force && sysInfoCached) { sysInfo.value = sysInfoCached; return }
  try { const info = await httpGet<HostSystemInfo>('/api/Common/Monitor/SystemInfo', undefined, 60_000); sysInfoCached = info; sysInfo.value = info } catch { sysInfo.value = null }
}
const sysInfoVisible = ref(false)
function openSysInfo() { sysInfoVisible.value = true; loadSystemInfo() }
function copyNetworkInfo() {
  const text = sysInfo.value?.networkText; if (!text) return
  navigator.clipboard.writeText(text).then(() => ElMessage.success('网络信息已复制到剪贴板')).catch(() => ElMessage.error('复制失败，请手动选择文本复制'))
}
function copySpecInfo() {
  const s = sysInfo.value; if (!s) return
  const text = [`设备名：${s.deviceName||'—'}`,`机型：${s.model||'—'}`,`处理器：${s.processor||'—'}`,`机带 RAM：${s.ram||'—'}`,`显卡：${s.gpu||'—'}`,`存储：${s.storage||'—'}`,`设备 ID：${s.deviceId||'—'}`,`产品 ID：${s.productId||'—'}`,`系统类型：${s.systemType||'—'}`,`笔和触控：${s.penTouch||'—'}`].join('\n')
  navigator.clipboard.writeText(text).then(() => ElMessage.success('设备规格已复制到剪贴板')).catch(() => ElMessage.error('复制失败，请手动选择文本复制'))
}

// ── 磁盘清理 ──
interface HostDiskClean { freedMb: number; freeGbAfter: number; deletedFiles: number; files: string[]; filesTruncated: boolean; items: { path: string; ok: boolean; reason: string }[] }
interface HostDiskFile { category: string; name: string; path: string; originalPath?: string; sizeKb: number; lastWriteTime: string }
interface HostDiskScan { files: HostDiskFile[]; recycleFiles: HostDiskFile[]; recycleCount: number; recycleSizeKb: number; truncated: boolean }
const CLEAN_OPTIONS = computed(() => cleanDrive.value === 'C' ? [
  { value: 'userTemp', label: '用户临时目录（%TEMP%）' }, { value: 'winTemp', label: 'Windows 临时目录（Windows\\Temp）' },
  { value: 'prefetch', label: 'Prefetch 预读缓存' }, { value: 'updateCache', label: 'Windows Update 下载缓存' },
  { value: 'browserCache', label: '浏览器缓存（Chrome/Edge/Firefox）' }, { value: 'thumbnailCache', label: '缩略图缓存（Explorer）' },
  { value: 'logFiles', label: '日志文件（*.log）' }, { value: 'oldDownloads', label: '旧下载文件（超过 30 天未访问）' },
] : [{ value: 'driveJunk', label: `${cleanDrive.value} 盘磁盘垃圾（*.tmp / *.temp / *.log / *.bak / *.chk / *.old）` }])
const CLEAN_CATEGORY_LABEL: Record<string, string> = { USER_TEMP: '用户临时', WIN_TEMP: 'Windows 临时', PREFETCH: 'Prefetch', UPDATE_CACHE: '更新缓存', BROWSER_CACHE: '浏览器缓存', THUMBNAIL_CACHE: '缩略图', LOG_FILE: '日志', OLD_DOWNLOAD: '旧下载', DRV_JUNK: '磁盘垃圾' }
const CLEAN_PALETTE = ['#409eff','#67c23a','#e6a23c','#f56c6c','#9b59b6','#16a085','#f39c12','#3498db','#e84393']
const CATEGORY_COLOR: Record<string, string> = { userTemp: '#409eff', winTemp: '#67c23a', prefetch: '#e6a23c', updateCache: '#f56c6c', browserCache: '#9b59b6', thumbnailCache: '#16a085', logFiles: '#f39c12', oldDownloads: '#3498db', recycleBin: '#e84393', driveJunk: '#7f8c8d' }
const CATEGORY_COLOR_BY_FILE_CAT: Record<string, string> = { USER_TEMP: CATEGORY_COLOR.userTemp, WIN_TEMP: CATEGORY_COLOR.winTemp, PREFETCH: CATEGORY_COLOR.prefetch, UPDATE_CACHE: CATEGORY_COLOR.updateCache, BROWSER_CACHE: CATEGORY_COLOR.browserCache, THUMBNAIL_CACHE: CATEGORY_COLOR.thumbnailCache, LOG_FILE: CATEGORY_COLOR.logFiles, OLD_DOWNLOAD: CATEGORY_COLOR.oldDownloads, DRV_JUNK: CATEGORY_COLOR.driveJunk }
function driveColor(letter: string): string { const idx = cleanDriveOptions.value.indexOf(letter); return CLEAN_PALETTE[(idx < 0 ? 0 : idx) % CLEAN_PALETTE.length] }
function toggleCategory(v: string) { const i = cleanCategories.value.indexOf(v); if (i >= 0) cleanCategories.value.splice(i, 1); else cleanCategories.value.push(v) }
const cleanVisible = ref(false)
const cleanStep = ref<'options' | 'files'>('options')
const cleaning = ref(false); const scanning = ref(false)
const cleanCategories = ref<string[]>([])
const cleanDrive = ref('C')
watch(cleanDrive, () => { cleanCategories.value = CLEAN_OPTIONS.value.map(o => o.value) })
const cleanDriveOptions = computed(() => { const letters = (metrics.value?.disks ?? []).map(d => d.drive.replace(/:$/, '').toUpperCase()).filter(l => /^[A-Z]$/.test(l)); return letters.length > 0 ? letters : ['C'] })
const scanData = ref<HostDiskScan | null>(null)
const scanSelectedKeys = ref<Set<string>>(new Set())
const recycleSelectedKeys = ref<Set<string>>(new Set())
const selectedFiles = computed(() => (scanData.value?.files ?? []).filter(f => scanSelectedKeys.value.has(f.path)))
const selectedRecycle = computed(() => (scanData.value?.recycleFiles ?? []).filter(f => recycleSelectedKeys.value.has(f.path)))
const cleanResult = ref<HostDiskClean | null>(null)
const cleanResultVisible = ref(false)
const scanTableHeight = ref(360); const recycleTableHeight = ref(240)
const recycleCollapsed = ref(true)
function toggleRecycleCollapse() { recycleCollapsed.value = !recycleCollapsed.value; calcCleanTableHeights() }
const fileCollapsed = ref(false)
function toggleFileCollapse() { fileCollapsed.value = !fileCollapsed.value; calcCleanTableHeights() }
function calcCleanTableHeights() {
  const total = Math.max(300, window.innerHeight - Math.round(window.innerHeight * 0.06) - 272)
  const TITLE = 36, showFile = hasFileCategories.value, fileExpanded = showFile && !fileCollapsed.value, recycleExpanded = !recycleCollapsed.value
  const free = Math.max(200, total - (showFile ? TITLE : 0) - TITLE)
  if (fileExpanded && recycleExpanded) { scanTableHeight.value = Math.round(free / 2); recycleTableHeight.value = free - scanTableHeight.value }
  else if (fileExpanded) { scanTableHeight.value = free; recycleTableHeight.value = 140 }
  else if (recycleExpanded) { scanTableHeight.value = 140; recycleTableHeight.value = free }
  else { scanTableHeight.value = 140; recycleTableHeight.value = 140 }
}
function onCleanResize() { if (cleanVisible.value && scanData.value) calcCleanTableHeights() }
onMounted(() => window.addEventListener('resize', onCleanResize))
onBeforeUnmount(() => window.removeEventListener('resize', onCleanResize))
const cleanWidthEl = ref<HTMLDivElement | null>(null); const cleanTableWidth = ref(0)
let cleanWidthRaf = 0; let cleanWidthRo: ResizeObserver | null = null
function setupCleanWidthRo() {
  cleanWidthRo?.disconnect(); cleanWidthRo = null; if (!cleanWidthEl.value) return
  cleanWidthRo = new ResizeObserver((entries) => { const w = Math.floor(entries[0]?.contentRect.width ?? 0); if (w <= 0 || w === cleanTableWidth.value) return; cancelAnimationFrame(cleanWidthRaf); cleanWidthRaf = requestAnimationFrame(() => { cleanTableWidth.value = w }) })
  cleanWidthRo.observe(cleanWidthEl.value)
}
watch(cleanStep, (v) => { if (v === 'files') nextTick(setupCleanWidthRo) })
onBeforeUnmount(() => { cleanWidthRo?.disconnect(); cancelAnimationFrame(cleanWidthRaf) })
const scanTotalKb = computed(() => (scanData.value?.files ?? []).reduce((s, f) => s + f.sizeKb, 0))
const scanKeyword = ref(''); const scanCatFilter = ref('')
const scanCatOptions = computed(() => { const files = scanData.value?.files ?? [], counts = new Map<string, number>(); files.forEach(f => counts.set(f.category, (counts.get(f.category) ?? 0) + 1)); return [...counts.entries()].map(([value, count]) => ({ value, count, label: CLEAN_CATEGORY_LABEL[value] ?? value })) })
const scanSortBy = ref<{ key: string; order: TableV2SortOrder } | null>(null)
function onScanSort({ key }: { key: string | number | symbol }) { const k = String(key), cur = scanSortBy.value; if (cur?.key !== k) scanSortBy.value = { key: k, order: TableV2SortOrder.DESC }; else if (cur.order === TableV2SortOrder.DESC) scanSortBy.value = { key: k, order: TableV2SortOrder.ASC }; else scanSortBy.value = null }
const filteredScanFiles = computed(() => {
  let files = scanData.value?.files ?? []
  if (scanCatFilter.value) files = files.filter(f => f.category === scanCatFilter.value)
  const kw = scanKeyword.value.trim().toLowerCase()
  if (kw) files = files.filter(f => f.path.toLowerCase().includes(kw) || f.name.toLowerCase().includes(kw) || (CLEAN_CATEGORY_LABEL[f.category] ?? f.category).toLowerCase().includes(kw))
  const sb = scanSortBy.value
  if (sb) { const dir = sb.order === TableV2SortOrder.ASC ? 1 : -1; files = [...files].sort((a, b) => (sb.key === 'size' ? a.sizeKb - b.sizeKb : String(a.lastWriteTime).localeCompare(String(b.lastWriteTime))) * dir) }
  return files
})
function toggleScanSel(path: string) { const s = new Set(scanSelectedKeys.value); if (s.has(path)) s.delete(path); else s.add(path); scanSelectedKeys.value = s }
watch([scanKeyword, scanCatFilter], () => { if (!scanData.value) return; scanSelectedKeys.value = new Set(filteredScanFiles.value.map(f => f.path)) })
function toggleScanAll() { const paths = filteredScanFiles.value.map(f => f.path), allSel = paths.length > 0 && paths.every(p => scanSelectedKeys.value.has(p)), s = new Set(scanSelectedKeys.value); paths.forEach(p => { if (allSel) s.delete(p); else s.add(p) }); scanSelectedKeys.value = s }
function toggleRecycleSel(path: string) { const s = new Set(recycleSelectedKeys.value); if (s.has(path)) s.delete(path); else s.add(path); recycleSelectedKeys.value = s }
function toggleRecycleAll() { const paths = (scanData.value?.recycleFiles ?? []).map(f => f.path), allSel = paths.length > 0 && paths.every(p => recycleSelectedKeys.value.has(p)), s = new Set(recycleSelectedKeys.value); paths.forEach(p => { if (allSel) s.delete(p); else s.add(p) }); recycleSelectedKeys.value = s }
let measureCtx: CanvasRenderingContext2D | null = null
function fitColumnWidth<T>(rows: T[], text: (row: T) => string, title: string, min: number, max: number): number {
  if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d')
  let w: number
  if (measureCtx) { measureCtx.font = '12px "Microsoft YaHei", sans-serif'; w = measureCtx.measureText(title).width; for (const row of rows) { if (w >= max) break; const tw = measureCtx.measureText(text(row)).width; if (tw > w) w = tw } } else { w = Math.max(title.length, 20) * 12 }
  return Math.max(min, Math.min(max, Math.ceil(w) + 30))
}
const FILE_ICON_MAP: { exts: string[]; icon: Component; cls: string }[] = [
  { exts: ['.png','.jpg','.jpeg','.gif','.bmp','.ico','.webp','.svg'], icon: Picture, cls: 'clean-ficon-green' },
  { exts: ['.mp4','.avi','.mkv','.mov','.wmv','.flv','.webm'], icon: VideoCamera, cls: 'clean-ficon-purple' },
  { exts: ['.mp3','.wav','.wma','.flac','.aac','.ogg','.m4a'], icon: Headset, cls: 'clean-ficon-cyan' },
  { exts: ['.zip','.rar','.7z','.gz','.tar','.cab','.iso'], icon: Box, cls: 'clean-ficon-yellow' },
  { exts: ['.exe','.dll','.msi','.sys','.drv','.ocx'], icon: Setting, cls: 'clean-ficon-blue' },
  { exts: ['.js','.ts','.vue','.css','.html','.htm','.json','.xml','.cs','.java','.py','.sql','.cmd','.ps1','.bat'], icon: Cpu, cls: 'clean-ficon-indigo' },
  { exts: ['.log','.txt','.ini','.cfg','.conf'], icon: Tickets, cls: 'clean-ficon-gray' },
  { exts: ['.tmp','.temp','.bak','.chk','.old','.dmp','.etl'], icon: Delete, cls: 'clean-ficon-red' },
]
function renderFileNameCell(name: string, icons: Record<string, string>) {
  const ext = name.includes('.') ? name.slice(name.lastIndexOf('.')).toLowerCase() : ''
  const url = icons[ext]
  const iconNode = url ? h('img', { src: url, class: 'clean-ficon-img', draggable: false }) : h(ElIcon, { class: ['clean-ficon', FILE_ICON_MAP.find(x => x.exts.includes(ext))?.cls ?? 'clean-ficon-default'] }, () => h(FILE_ICON_MAP.find(x => x.exts.includes(ext))?.icon ?? Document))
  return h('span', { class: 'clean-fname', title: name }, [iconNode, h('span', { class: 'clean-fname-text' }, name)])
}
const fileIconMap = shallowRef<Record<string, string>>({})
const iconRequestedExts = new Set<string>()
async function loadFileIcons() {
  if (!scanData.value) return
  const exts = new Set<string>(); for (const f of [...scanData.value.files, ...(scanData.value.recycleFiles ?? [])]) { const i = f.name.lastIndexOf('.'); if (i >= 0) exts.add(f.name.slice(i).toLowerCase()) }
  const missing = [...exts].filter(e => !iconRequestedExts.has(e)); if (missing.length === 0) return
  missing.forEach(e => iconRequestedExts.add(e))
  try { const res = await httpGet<Record<string, string>>('/api/Common/Monitor/FileIcons', { exts: missing.join(',') }, undefined, { silent: true }); if (res && Object.keys(res).length > 0) fileIconMap.value = { ...fileIconMap.value, ...res } } catch { /* 静默降级 */ }
}
const scanColumns = computed<any[]>(() => {
  const list = filteredScanFiles.value, all = scanData.value?.files ?? [], icons = fileIconMap.value
  const selCount = list.reduce((n, f) => n + (scanSelectedKeys.value.has(f.path) ? 1 : 0), 0), allSelected = list.length > 0 && selCount === list.length
  return [
    { key: 'sel', width: 45, align: 'center', fixed: 'left', headerCellRenderer: () => h(ElCheckbox, { modelValue: allSelected, indeterminate: selCount > 0 && !allSelected, onChange: toggleScanAll }), cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h(ElCheckbox, { modelValue: scanSelectedKeys.value.has(rowData.path), onChange: () => toggleScanSel(rowData.path) }) },
    { key: 'idx', title: '#', width: 50, align: 'center', fixed: 'left', cellRenderer: ({ rowIndex }: { rowIndex: number }) => h('span', String(rowIndex + 1)) },
    { key: 'cat', title: '分类', width: fitColumnWidth(all, f => CLEAN_CATEGORY_LABEL[f.category] ?? f.category, '分类', 100, 150) + 16, align: 'center', fixed: 'left', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => { const c = CATEGORY_COLOR_BY_FILE_CAT[rowData.category] ?? '#909399'; return h('span', { class: 'clean-cat', style: { color: c } }, [h('span', { style: { display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: c, marginRight: '5px', verticalAlign: 'middle' } }), CLEAN_CATEGORY_LABEL[rowData.category] ?? rowData.category]) } },
    { key: 'name', dataKey: 'name', title: '文件名称', width: fitColumnWidth(all, f => f.name, '文件名称', 160, 360) + 20, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => renderFileNameCell(rowData.name, icons) },
    { key: 'path', dataKey: 'path', title: '文件路径', width: fitColumnWidth(all, f => f.path, '文件路径', 260, 800), flexGrow: 2, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', { class: 'clean-cell-ellipsis', title: rowData.path }, rowData.path) },
    { key: 'size', title: '大小', width: fitColumnWidth(all, f => fmtSize(f.sizeKb), '大小', 90, 160), align: 'center', sortable: true, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtSize(rowData.sizeKb)) },
    { key: 'mtime', title: '最后修改', width: fitColumnWidth(all, f => formatDate(f.lastWriteTime), '最后修改', 140, 180), align: 'center', sortable: true, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', formatDate(rowData.lastWriteTime)) },
    { key: 'op', title: '操作', width: 90, align: 'center', fixed: 'right', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h(ElButton, { link: true, type: 'primary', size: 'small', onClick: () => openFolder(rowData) }, () => '打开文件夹') },
  ]
})
const recycleColumns = computed<any[]>(() => {
  const list = scanData.value?.recycleFiles ?? [], icons = fileIconMap.value
  const selCount = list.reduce((n, f) => n + (recycleSelectedKeys.value.has(f.path) ? 1 : 0), 0), allSelected = list.length > 0 && selCount === list.length
  return [
    { key: 'sel', width: 45, align: 'center', fixed: 'left', headerCellRenderer: () => h(ElCheckbox, { modelValue: allSelected, indeterminate: selCount > 0 && !allSelected, onChange: toggleRecycleAll }), cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h(ElCheckbox, { modelValue: recycleSelectedKeys.value.has(rowData.path), onChange: () => toggleRecycleSel(rowData.path) }) },
    { key: 'idx', title: '#', width: 50, align: 'center', fixed: 'left', cellRenderer: ({ rowIndex }: { rowIndex: number }) => h('span', String(rowIndex + 1)) },
    { key: 'name', dataKey: 'name', title: '名称', width: fitColumnWidth(list, f => f.name, '名称', 180, 400) + 20, flexGrow: 1, fixed: 'left', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => renderFileNameCell(rowData.name, icons) },
    { key: 'orig', title: '原位置', width: fitColumnWidth(list, f => f.originalPath || f.path, '原位置', 200, 800), flexGrow: 1, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', { class: 'clean-cell-ellipsis', title: rowData.originalPath || rowData.path }, rowData.originalPath || rowData.path) },
    { key: 'size', title: '大小', width: fitColumnWidth(list, f => fmtSize(f.sizeKb), '大小', 90, 160), align: 'center', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtSize(rowData.sizeKb)) },
    { key: 'dtime', title: '删除时间', width: fitColumnWidth(list, f => fmtDeleted(f.lastWriteTime), '删除时间', 140, 180), align: 'center', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtDeleted(rowData.lastWriteTime)) },
  ]
})
const selectedKb = computed(() => selectedFiles.value.reduce((s, f) => s + f.sizeKb, 0))
const FILE_CATEGORY_VALUES = ['userTemp','winTemp','prefetch','updateCache','browserCache','thumbnailCache','logFiles','oldDownloads','driveJunk']
const hasFileCategories = computed(() => FILE_CATEGORY_VALUES.some(v => cleanCategories.value.includes(v)))
const cleanButtonText = computed(() => { const parts: string[] = []; if (selectedFiles.value.length > 0) parts.push(`${selectedFiles.value.length} 个文件 / ${fmtSize(selectedKb.value)}`); if (selectedRecycle.value.length > 0) parts.push(`${selectedRecycle.value.length} 项回收站`); return parts.length > 0 ? `清理选中项（${parts.join('，')}）` : '清理选中项' })
const scanPieOption = computed(() => {
  const files = scanData.value?.files ?? [], byCategory = new Map<string, { count: number; sizeKb: number }>()
  for (const f of files) { const label = CLEAN_CATEGORY_LABEL[f.category] ?? f.category, entry = byCategory.get(label) ?? { count: 0, sizeKb: 0 }; entry.count++; entry.sizeKb += f.sizeKb; byCategory.set(label, entry) }
  const data = Array.from(byCategory.entries()).map(([name, v]) => ({ name: `${name} (${v.count} 个)`, value: Math.round(v.sizeKb) }))
  return { tooltip: { trigger: 'item' as const, confine: true, formatter: (p: { name: string; value: number; percent: number }) => `${p.name}<br/>大小：${fmtSize(p.value)} KB<br/>占比：${p.percent}%` }, legend: { orient: 'horizontal' as const, left: 'center' as const, bottom: 0, itemWidth: 10, itemHeight: 10, itemGap: 8, textStyle: { fontSize: 11 } }, series: [{ name: '文件占用', type: 'pie' as const, radius: ['26%','45%'], center: ['50%','32%'], avoidLabelOverlap: true, itemStyle: { borderRadius: 6, borderColor: '#fff', borderWidth: 2 }, label: { show: false }, emphasis: { label: { show: true, fontSize: 14, fontWeight: 'bold' as const } }, data }] }
})
function fmtSize(kb: number): string { if (kb >= 1024 * 1024) return `${(kb / 1024 / 1024).toFixed(2)} GB`; if (kb >= 1024) return `${(kb / 1024).toFixed(1)} MB`; return `${Math.round(kb)} KB` }
function fmtDeleted(t: string): string { return t && !t.startsWith('0001') ? formatDate(t) : '—' }
const recycleOriginalMap = computed(() => { const m = new Map<string, string>(); for (const f of scanData.value?.recycleFiles ?? []) { if (f.path) m.set(f.path.toLowerCase(), f.originalPath || f.path) }; return m })
function recycleDisplayPath(p: string): string { return recycleOriginalMap.value.get(p.toLowerCase()) ?? p }
function openCleanDisk() {
  cleanDrive.value = cleanDriveOptions.value.includes('C') ? 'C' : cleanDriveOptions.value[0]
  cleanCategories.value = CLEAN_OPTIONS.value.map(o => o.value); cleanStep.value = 'options'; scanData.value = null; scanKeyword.value = ''
  recycleCollapsed.value = true; fileCollapsed.value = false; scanSelectedKeys.value = new Set(); recycleSelectedKeys.value = new Set(); cleanVisible.value = true
}
interface ScanJob { jobId: string; done: boolean; scannedCount: number; foundKb: number; error?: string | null; result?: HostDiskScan | null }
const scanProgress = ref<{ count: number; kb: number } | null>(null)
async function rescan() { if (scanning.value || cleaning.value) return; scanKeyword.value = ''; scanCatFilter.value = ''; scanSortBy.value = null; await scanFiles() }
async function scanFiles() {
  if (cleanCategories.value.length === 0) return
  scanning.value = true; scanProgress.value = { count: 0, kb: 0 }
  try {
    let start: ScanJob
    try { start = await httpGet<ScanJob>('/api/Common/Monitor/ScanDiskStart', { categories: cleanCategories.value.join(','), drive: cleanDrive.value }, undefined, { silent: true }) } catch { ElMessage.error('扫描启动失败，请重试'); return }
    while (cleanVisible.value) {
      await new Promise(r => setTimeout(r, 600))
      let p: ScanJob; try { p = await httpGet<ScanJob>('/api/Common/Monitor/ScanProgress', { jobId: start.jobId }, undefined, { silent: true }) } catch { ElMessage.error('扫描进度获取失败，请重试'); return }
      scanProgress.value = { count: p.scannedCount, kb: p.foundKb }; if (!p.done) continue
      if (p.error) { ElMessage.error(p.error); return }; scanData.value = p.result ?? null; break
    }
    if (!scanData.value) return
    scanKeyword.value = ''; scanCatFilter.value = ''; scanSortBy.value = null; recycleCollapsed.value = true; fileCollapsed.value = false
    scanSelectedKeys.value = new Set((scanData.value?.files ?? []).map(f => f.path)); recycleSelectedKeys.value = new Set()
    loadFileIcons(); calcCleanTableHeights(); cleanStep.value = 'files'
  } finally { scanning.value = false; scanProgress.value = null }
}
async function openFolderAt(filePath: string) { if (isHostAvailable()) { hostOpenLocation(filePath); return }; await httpPost(`/api/Common/Monitor/OpenFolder?path=${encodeURIComponent(filePath)}`, {}) }
async function openFolder(row: HostDiskFile) { await openFolderAt(row.path) }
async function openRecycleBin() { if (isHostAvailable()) { hostOpenRecycleBin(); return }; await httpPost('/api/Common/Monitor/OpenRecycleBin', {}) }
interface CleanJob { jobId: string; done: boolean; totalCount: number; deletedCount: number; freedMb: number; error?: string | null; result?: HostDiskClean | null }
const cleanProgress = ref<{ deleted: number; total: number; freedMb: number } | null>(null)
const cleanPercent = computed(() => { const p = cleanProgress.value; if (!p || p.total <= 0) return 0; return Math.min(100, Math.round((p.deleted / p.total) * 100)) })
async function doCleanDisk() {
  const hasCat = (v: string) => cleanCategories.value.includes(v)
  cleaning.value = true; cleanProgress.value = { deleted: 0, total: (hasFileCategories.value ? selectedFiles.value.length : 0) + selectedRecycle.value.length, freedMb: 0 }
  try {
    let start: CleanJob
    try { start = await httpPost<CleanJob>('/api/Common/Monitor/CleanDiskStart', { drive: cleanDrive.value, userTemp: hasCat('userTemp'), windowsTemp: hasCat('winTemp'), prefetch: hasCat('prefetch'), updateCache: hasCat('updateCache'), browserCache: hasCat('browserCache'), thumbnailCache: hasCat('thumbnailCache'), logFiles: hasCat('logFiles'), oldDownloads: hasCat('oldDownloads'), driveJunk: hasCat('driveJunk'), recycleBin: selectedRecycle.value.length > 0, paths: hasFileCategories.value ? selectedFiles.value.map(f => f.path) : [], pathCategories: Object.fromEntries(selectedFiles.value.map(f => [f.path, f.category])), recyclePaths: selectedRecycle.value.map(f => f.path) }, undefined, undefined, { silent: true }) } catch { ElMessage.error('清理启动失败，请重试'); return }
    while (cleanVisible.value) {
      await new Promise(r => setTimeout(r, 600))
      let p: CleanJob; try { p = await httpGet<CleanJob>('/api/Common/Monitor/CleanProgress', { jobId: start.jobId }, undefined, { silent: true }) } catch { ElMessage.error('清理进度获取失败，请重试'); return }
      cleanProgress.value = { deleted: p.deletedCount, total: p.totalCount > 0 ? p.totalCount : (cleanProgress.value?.total ?? 0), freedMb: p.freedMb }
      if (!p.done) continue; if (p.error) { ElMessage.error(p.error); return }
      cleanVisible.value = false; cleanResult.value = p.result ?? null; cleanResultVisible.value = true; loadOverview(); break
    }
  } finally { cleaning.value = false; cleanProgress.value = null }
}
</script>
<template>
  <div class="monitor-page">
    <div class="host-dashboard">
      <div class="host-dashboard-bar">
        <span class="host-dashboard-title">本机监控 Dashboard</span>
        <el-button size="small" :loading="overviewLoading" @click="loadOverview()">刷新</el-button>
        <el-button v-if="has('local-monitor:clean-disk')" size="small" type="primary" plain :loading="cleaning" @click="openCleanDisk">清理磁盘</el-button>
      </div>
      <template v-if="metrics">
        <div class="host-stat-row">
          <div class="host-stat"><div class="host-stat-value">{{ metrics.uptimeHours != null ? uptimeText(metrics.uptimeHours) : '—' }}</div><div class="host-stat-label">运行时间</div></div>
          <div class="host-stat"><div class="host-stat-value">{{ metrics.cpuCores ?? '—' }}</div><div class="host-stat-label">CPU 核数</div></div>
          <div class="host-stat"><div class="host-stat-value">{{ metrics.memoryTotalGb != null ? `${metrics.memoryTotalGb} GB` : '—' }}</div><div class="host-stat-label">总内存</div></div>
          <div class="host-stat"><div class="host-stat-value">{{ metrics.processCount ?? '—' }}</div><div class="host-stat-label">进程数</div></div>
          <div class="host-stat host-stat-wide host-stat-clickable" title="点击查看设备规格与网络信息" @click="openSysInfo">
            <div class="host-stat-value host-stat-os" :title="metrics.osName ?? ''">{{ metrics.osName || '—' }}</div>
            <div class="host-stat-label">操作系统（点击查看设备规格 / 网络信息）</div>
          </div>
        </div>
        <div class="host-disk-panel">
          <div class="host-disk-header"><div class="host-chart-title">各分区可用空间</div></div>
          <el-table :data="metrics.disks ?? []" size="small" border>
            <el-table-column prop="drive" label="分区" width="90" />
            <el-table-column label="总空间" width="110"><template #default="{ row }">{{ (row as HostMetrics['disks'][number]).totalGb }} GB</template></el-table-column>
            <el-table-column label="可用空间" width="110"><template #default="{ row }">{{ (row as HostMetrics['disks'][number]).freeGb }} GB</template></el-table-column>
            <el-table-column label="使用率" min-width="240"><template #default="{ row }"><el-progress :percentage="(row as HostMetrics['disks'][number]).usedPercent" :color="percentColor((row as HostMetrics['disks'][number]).usedPercent)" :stroke-width="12" /></template></el-table-column>
          </el-table>
        </div>
        <div class="host-metric-cards">
          <div class="host-metric-card"><div class="host-metric-label">CPU 使用率</div><div class="host-metric-value" :style="{ color: metrics.cpuPercent != null ? percentColor(metrics.cpuPercent) : '' }">{{ metrics.cpuPercent != null ? `${metrics.cpuPercent}%` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">内存使用率</div><div class="host-metric-value" :style="{ color: metrics.memoryPercent != null ? percentColor(metrics.memoryPercent) : '' }">{{ metrics.memoryPercent != null ? `${metrics.memoryPercent}%` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">已用内存</div><div class="host-metric-value">{{ metrics.memoryUsedGb != null ? `${metrics.memoryUsedGb} GB` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">网络接收</div><div class="host-metric-value">{{ metrics.netInKbps != null ? `${metrics.netInKbps} KB/s` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">网络发送</div><div class="host-metric-value">{{ metrics.netOutKbps != null ? `${metrics.netOutKbps} KB/s` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">磁盘读取</div><div class="host-metric-value">{{ metrics.diskReadMbPerSec != null ? `${metrics.diskReadMbPerSec} MB/s` : '—' }}</div></div>
          <div class="host-metric-card"><div class="host-metric-label">磁盘写入</div><div class="host-metric-value">{{ metrics.diskWriteMbPerSec != null ? `${metrics.diskWriteMbPerSec} MB/s` : '—' }}</div></div>
        </div>
      </template>
      <div v-else v-loading="overviewLoading" class="host-card-empty">暂无指标数据，点击"刷新"立即采集</div>
    </div>
    <CommonDialog v-model="sysInfoVisible" title="设备规格与网络信息" width="960px" destroy-on-close>
      <div class="sysinfo-dialog-body">
        <div class="sysinfo-net-header sysinfo-spec-header">
          <div class="host-chart-title">设备规格</div>
          <div class="sysinfo-header-actions">
            <CommonTooltip content="重新加载" :copyable="false"><el-icon class="netinfo-copy" @click="loadSystemInfo(true)"><Refresh /></el-icon></CommonTooltip>
            <CommonTooltip content="复制设备规格" :copyable="false"><el-icon class="netinfo-copy" :class="{ disabled: !sysInfo }" @click="copySpecInfo"><CopyDocument /></el-icon></CommonTooltip>
          </div>
        </div>
        <div class="sysinfo-grid">
          <div class="sysinfo-item"><span class="sysinfo-label">设备名</span><span class="sysinfo-value">{{ sysInfo?.deviceName || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">机型</span><span class="sysinfo-value">{{ sysInfo?.model || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">处理器</span><span class="sysinfo-value">{{ sysInfo?.processor || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">机带 RAM</span><span class="sysinfo-value">{{ sysInfo?.ram || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">显卡</span><span class="sysinfo-value">{{ sysInfo?.gpu || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">存储</span><span class="sysinfo-value">{{ sysInfo?.storage || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">设备 ID</span><span class="sysinfo-value">{{ sysInfo?.deviceId || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">产品 ID</span><span class="sysinfo-value">{{ sysInfo?.productId || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">系统类型</span><span class="sysinfo-value">{{ sysInfo?.systemType || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">笔和触控</span><span class="sysinfo-value">{{ sysInfo?.penTouch || '—' }}</span></div>
        </div>
        <div class="sysinfo-net-header">
          <div class="host-chart-title">网络信息（ipconfig /all）</div>
          <CommonTooltip content="复制网络信息" :copyable="false"><el-icon class="netinfo-copy" :class="{ disabled: !sysInfo?.networkText }" @click="copyNetworkInfo"><CopyDocument /></el-icon></CommonTooltip>
        </div>
        <pre class="netinfo-pre">{{ sysInfo?.networkText || '—' }}</pre>
      </div>
    </CommonDialog>
    <CommonDialog v-model="cleanVisible" :title="cleanStep === 'options' ? '清理磁盘' : '清理磁盘 · 勾选要删除的文件'" :width="cleanStep === 'options' ? '680px' : 'calc(100vw - 120px)'" class="clean-scan-dialog" destroy-on-close>
      <template v-if="cleanStep === 'options'">
        <div class="clean-step-wrap">
          <el-alert type="info" :closable="false" show-icon>扫描将列出勾选清理类别下的全部文件，不会删除任何文件，下一步可勾选要删除哪些；回收站无需勾选，扫描默认列出全部盘数据</el-alert>
          <div class="clean-sec-title">清理盘符</div>
          <div class="clean-drive-cards"><div v-for="l in cleanDriveOptions" :key="l" class="clean-drive-card" :class="{ active: cleanDrive === l }" :style="{ '--c': driveColor(l) }" @click="cleanDrive = l"><span class="clean-drive-letter">{{ l }}</span><span class="clean-drive-sub">{{ l }} 盘</span></div></div>
          <div v-if="cleanDrive !== 'C'" class="clean-drive-hint">{{ cleanDrive }} 盘为非系统盘：系统缓存类清理项只存在于系统盘，这里仅展示该盘可用的清理项</div>
          <div class="clean-sec-title">清理内容（可多选）</div>
          <div class="clean-cat-cards"><div v-for="opt in CLEAN_OPTIONS" :key="opt.value" class="clean-cat-card" :class="{ active: cleanCategories.includes(opt.value) }" :style="{ '--c': CATEGORY_COLOR[opt.value] ?? '#409eff' }" @click="toggleCategory(opt.value)"><span class="clean-cat-dot"></span><span class="clean-cat-name">{{ opt.label }}</span><span class="clean-cat-check">✓</span></div></div>
          <div v-if="scanning" class="clean-dialog-mask"><div class="clean-mask-panel"><el-progress :percentage="100" :indeterminate="true" :show-text="false" :stroke-width="10" /><div class="clean-mask-text">正在扫描：已扫描 <b>{{ scanProgress?.count ?? 0 }}</b> 个文件，已发现 <b>{{ fmtSize(scanProgress?.kb ?? 0) }}</b>…</div></div></div>
        </div>
      </template>
      <template v-else-if="scanData">
        <div class="clean-step-wrap">
          <div v-if="hasFileCategories" class="clean-scan-body">
            <div class="clean-scan-side">
              <div class="clean-summary">扫描到 <b>{{ scanData.files.length }}</b> 个文件，共 <b>{{ fmtSize(scanTotalKb) }}</b>；<br/>回收站 <b>{{ scanData.recycleCount }}</b> 项（约 {{ fmtSize(scanData.recycleSizeKb) }}）<span v-if="scanData.truncated" class="clean-trunc">（仅展示前 3000 条）</span></div>
              <div v-if="scanData.files.length > 0" class="scan-pie-chart"><BaseChart :option="scanPieOption" height="330px" /></div>
            </div>
            <div ref="cleanWidthEl" class="clean-scan-tables">
              <div class="clean-scan-toolbar">
                <el-input v-model="scanKeyword" size="small" clearable placeholder="搜索文件路径 / 名称 / 分类" :prefix-icon="Search" class="clean-scan-search" />
                <el-select v-model="scanCatFilter" size="small" clearable placeholder="全部分类" class="clean-scan-cat-filter"><el-option v-for="c in scanCatOptions" :key="c.value" :value="c.value" :label="`${c.label}（${c.count} 个）`"><span class="clean-cat-filter-dot" :style="{ background: CATEGORY_COLOR_BY_FILE_CAT[c.value] ?? '#909399' }"></span>{{ c.label }}（{{ c.count }} 个）</el-option></el-select>
                <span v-if="scanKeyword.trim() || scanCatFilter" class="clean-scan-toolbar-hint">匹配 {{ filteredScanFiles.length }} / {{ scanData.files.length }} 条</span>
                <el-button size="small" :icon="Refresh" :disabled="scanning || cleaning" class="clean-rescan-btn" @click="rescan">重新扫描</el-button>
              </div>
              <div class="clean-recycle-title clean-recycle-toggle" @click="toggleFileCollapse"><span class="clean-recycle-arrow">{{ fileCollapsed ? '▶' : '▼' }}</span>候选文件（删除勾选项）：{{ filteredScanFiles.length }} 个<span class="clean-recycle-hint">{{ fileCollapsed ? '点击展开' : '点击折叠' }}</span></div>
              <div v-show="!fileCollapsed" :style="{ height: scanTableHeight + 'px', overflow: 'hidden' }"><el-table-v2 v-if="cleanTableWidth > 0" :columns="scanColumns" :data="filteredScanFiles" :width="cleanTableWidth" :height="scanTableHeight" :row-height="36" row-key="path" fixed :sort-by="scanSortBy ?? undefined" class="clean-virtual-table" empty-text="勾选项中没有可清理的文件" @column-sort="onScanSort" /></div>
              <div class="clean-recycle-title clean-recycle-toggle" @click="toggleRecycleCollapse"><span class="clean-recycle-arrow">{{ recycleCollapsed ? '▶' : '▼' }}</span>回收站内容（全部盘，删除勾选项）：{{ scanData.recycleCount }} 项<span class="clean-recycle-hint">{{ recycleCollapsed ? '点击展开' : '点击折叠' }}</span><el-button size="small" class="clean-open-recycle-btn" @click.stop="openRecycleBin">回收站</el-button></div>
              <div v-show="!recycleCollapsed" :style="{ height: recycleTableHeight + 'px', overflow: 'hidden' }"><el-table-v2 v-if="cleanTableWidth > 0" :columns="recycleColumns" :data="scanData.recycleFiles" :width="cleanTableWidth" :height="recycleTableHeight" :row-height="36" row-key="path" fixed class="clean-virtual-table" empty-text="回收站当前为空" /></div>
            </div>
          </div>
          <template v-else>
            <div class="clean-summary">本次清理 <b>{{ cleanDrive }}</b> 盘回收站：共 <b>{{ scanData.recycleCount }}</b> 项（约 {{ fmtSize(scanData.recycleSizeKb) }}），删除勾选项</div>
            <div ref="cleanWidthEl" :style="{ height: recycleTableHeight + 'px', overflow: 'hidden' }"><el-table-v2 v-if="cleanTableWidth > 0" :columns="recycleColumns" :data="scanData.recycleFiles" :width="cleanTableWidth" :height="recycleTableHeight" :row-height="36" row-key="path" fixed class="clean-virtual-table" empty-text="回收站当前为空" /></div>
          </template>
          <div v-if="scanning" class="clean-dialog-mask"><div class="clean-mask-panel"><el-progress :percentage="100" :indeterminate="true" :show-text="false" :stroke-width="10" /><div class="clean-mask-text">正在扫描：已扫描 <b>{{ scanProgress?.count ?? 0 }}</b> 个，已发现 <b>{{ fmtSize(scanProgress?.kb ?? 0) }}</b>…</div></div></div>
          <div v-if="cleaning && cleanProgress" class="clean-dialog-mask"><div class="clean-mask-panel"><el-progress :percentage="cleanPercent" :indeterminate="cleanProgress.total === 0" :show-text="false" :stroke-width="10" /><div class="clean-mask-text">正在清理：已删除 <b>{{ cleanProgress.deleted }}</b><template v-if="cleanProgress.total > 0"> / {{ cleanProgress.total }}</template> 个 · 已释放 <b>{{ cleanProgress.freedMb.toFixed(1) }}</b> MB…</div></div></div>
        </div>
      </template>
      <template #footer>
        <template v-if="cleanStep === 'options'"><el-button @click="cleanVisible = false">取消</el-button><el-button type="primary" :loading="scanning" :disabled="cleanCategories.length === 0" @click="scanFiles">扫描文件</el-button></template>
        <template v-else><el-button :disabled="cleaning" @click="cleanStep = 'options'">上一步</el-button><el-button type="primary" :loading="cleaning" :disabled="cleaning || (selectedFiles.length === 0 && selectedRecycle.length === 0)" @click="doCleanDisk">{{ cleanButtonText }}</el-button></template>
      </template>
    </CommonDialog>
    <CommonDialog v-model="cleanResultVisible" title="清理结果" width="620px" destroy-on-close>
      <template v-if="cleanResult">
        <div class="clean-result-header"><div class="clean-result-icon">✔</div><div class="clean-result-stats"><div class="clean-stat-card"><span class="clean-stat-num">{{ cleanResult.freedMb }}</span><span class="clean-stat-label">释放 MB</span></div><div class="clean-stat-card"><span class="clean-stat-num">{{ cleanResult.deletedFiles }}</span><span class="clean-stat-label">删除文件数</span></div><div class="clean-stat-card"><span class="clean-stat-num">{{ cleanResult.freeGbAfter }}</span><span class="clean-stat-label">{{ cleanDrive }} 盘剩余 GB</span></div></div></div>
        <span v-if="cleanResult.filesTruncated" class="clean-trunc">文件清单仅展示前 2000 条</span>
        <div v-if="cleanResult.items.length" class="clean-file-list"><div v-for="(it, i) in cleanResult.items" :key="i" class="clean-file-item clean-item-row"><span :class="it.ok ? 'clean-item-ok' : 'clean-item-fail'">{{ it.ok ? '✔ 已删除' : '✘ ' + it.reason }}</span><span class="clean-item-path" :title="it.path">{{ recycleDisplayPath(it.path) }}</span></div></div>
        <div v-else-if="cleanResult.files.length" class="clean-file-list"><div v-for="(f, i) in cleanResult.files" :key="i" class="clean-file-item">{{ i + 1 }}. {{ recycleDisplayPath(f) }}</div></div>
        <div v-else class="clean-file-empty">本次勾选项中没有可删除的文件</div>
      </template>
    </CommonDialog>
  </div>
</template>
<style scoped>
.monitor-page { height: 100%; display: flex; flex-direction: column; overflow: hidden; }
.host-dashboard { flex: 1; min-height: 0; overflow-y: auto; padding: 0 16px 16px; }
.host-dashboard-bar { position: sticky; top: 0; z-index: 10; display: flex; align-items: center; gap: 10px; margin: 0 -16px 14px; padding: 10px 16px; background: var(--page-bg, #f2f5f7); border-bottom: 1px solid #e4e7ed; }
.host-dashboard-title { font-size: 14px; font-weight: 600; color: #303133; }
.host-stat-row { display: flex; gap: 12px; margin-bottom: 14px; }
.host-stat { flex: 0 0 auto; min-width: 110px; background: #fff; border: 1px solid #ebeef5; border-radius: 12px; padding: 12px 16px; text-align: center; box-shadow: 0 1px 4px rgba(0,0,0,.04); }
.host-stat-wide { flex: 1; min-width: 0; }
.host-stat-value { font-size: 20px; font-weight: 600; color: #2f9e44; line-height: 1.4; }
.host-stat-os { font-size: 13px; font-weight: 500; color: #303133; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.host-stat-label { margin-top: 2px; font-size: 12px; color: #909399; }
.host-metric-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 12px; margin-bottom: 14px; }
.host-metric-card { background: #fff; border: 1px solid #ebeef5; border-radius: 12px; padding: 14px 16px; text-align: center; box-shadow: 0 1px 4px rgba(0,0,0,.04); }
.host-metric-label { font-size: 12px; color: #909399; margin-bottom: 6px; }
.host-metric-value { font-size: 22px; font-weight: 700; color: #303133; }
.sysinfo-dialog-body { min-height: 200px; }
.sysinfo-net-header { display: flex; align-items: center; justify-content: space-between; margin-top: 16px; }
.sysinfo-header-actions { display: flex; align-items: center; gap: 12px; }
.sysinfo-spec-header { margin-top: 0; }
.netinfo-copy { font-size: 17px; color: #909399; cursor: pointer; transition: color .2s; }
.netinfo-copy:hover { color: #409eff; }
.netinfo-copy.disabled { color: #c0c4cc; cursor: not-allowed; }
.host-stat-clickable { cursor: pointer; transition: border-color .2s, box-shadow .2s; }
.host-stat-clickable:hover { border-color: #409eff; box-shadow: 0 2px 8px rgba(64,158,255,.18); }
.sysinfo-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px 24px; padding: 4px 2px; }
.sysinfo-item { display: flex; gap: 12px; font-size: 13px; line-height: 1.8; }
.sysinfo-label { flex: 0 0 64px; color: #909399; }
.sysinfo-value { color: #303133; word-break: break-all; }
.netinfo-pre { margin: 0; padding: 10px 12px; max-height: 340px; overflow: auto; background: #f7f8fa; border-radius: 8px; font-size: 12px; line-height: 1.6; font-family: Consolas, 'Courier New', monospace; white-space: pre; }
.host-chart-title { font-size: 13px; font-weight: 600; color: #606266; margin-bottom: 4px; }
.host-disk-panel { margin-bottom: 14px; background: #fff; border: 1px solid #ebeef5; border-radius: 12px; padding: 12px 14px; box-shadow: 0 1px 4px rgba(0,0,0,.04); }
.host-disk-panel .host-chart-title { margin-bottom: 0; }
.host-disk-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.host-card-empty { padding: 10px 0; font-size: 12px; color: #909399; }
.clean-scan-dialog { --el-dialog-margin-top: 6vh; }
.clean-scan-body { display: flex; gap: 16px; align-items: flex-start; }
.clean-scan-side { flex: 0 0 300px; }
.clean-scan-tables { flex: 1; min-width: 0; }
.clean-virtual-table :deep(.clean-cat) { display: inline-block; padding: 1px 8px; font-size: 12px; color: #606266; background: #f0f2f5; border-radius: 4px; }
.clean-sec-title { margin: 16px 0 10px; font-size: 13px; font-weight: 600; color: #303133; }
.clean-drive-cards { display: flex; gap: 12px; flex-wrap: wrap; }
.clean-drive-hint { margin-top: 6px; font-size: 12px; color: #e6a23c; }
.clean-drive-card { width: 84px; padding: 12px 0; display: flex; flex-direction: column; align-items: center; gap: 4px; border: 1px solid #e4e7ed; border-radius: 10px; background: #fff; cursor: pointer; transition: all .15s; }
.clean-drive-card:hover { border-color: var(--c); }
.clean-drive-card.active { border-color: var(--c); box-shadow: 0 0 0 1px var(--c) inset; background: color-mix(in srgb, var(--c) 8%, #fff); }
.clean-drive-letter { font-size: 22px; font-weight: 700; line-height: 1; color: var(--c); }
.clean-drive-sub { font-size: 12px; color: #909399; }
.clean-drive-card.active .clean-drive-sub { color: var(--c); }
.clean-cat-cards { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
.clean-cat-card { position: relative; display: flex; align-items: center; gap: 8px; padding: 11px 22px 11px 12px; border: 1px solid #e4e7ed; border-radius: 10px; background: #fff; cursor: pointer; font-size: 13px; color: #606266; transition: all .15s; }
.clean-cat-card:hover { border-color: var(--c); }
.clean-cat-card.active { border-color: var(--c); box-shadow: 0 0 0 1px var(--c) inset; background: color-mix(in srgb, var(--c) 8%, #fff); color: #303133; }
.clean-cat-dot { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--c); }
.clean-cat-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.clean-cat-check { position: absolute; top: 4px; right: 8px; font-size: 11px; color: var(--c); display: none; }
.clean-cat-card.active .clean-cat-check { display: block; }
.clean-step-wrap { position: relative; }
.clean-dialog-mask { position: absolute; inset: 0; z-index: 10; display: flex; align-items: center; justify-content: center; background: rgba(255,255,255,.75); }
.clean-mask-panel { display: flex; flex-direction: column; align-items: center; gap: 14px; width: 80%; max-width: 480px; }
.clean-mask-panel .el-progress { width: 100%; align-self: stretch; }
.clean-mask-text { font-size: 12px; color: #606266; }
.clean-recycle-title { margin: 6px 0; font-size: 13px; color: #606266; }
.clean-scan-toolbar { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
.clean-scan-search { width: 280px; }
.clean-scan-cat-filter { width: 170px; }
.clean-cat-filter-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 6px; vertical-align: middle; }
.clean-scan-toolbar-hint { font-size: 12px; color: #909399; }
.clean-rescan-btn { margin-left: auto; }
.clean-virtual-table { border: 1px solid var(--el-border-color-lighter); }
.clean-virtual-table :deep(.el-table-v2__header-cell) { background: #f5f7fa; color: #606266; font-weight: 600; font-size: 12px; }
.clean-virtual-table :deep(.el-table-v2__row-cell) { font-size: 12px; border-bottom: 1px solid var(--el-border-color-lighter); }
.clean-virtual-table :deep(.clean-cell-ellipsis) { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 100%; min-width: 0; }
.clean-virtual-table :deep(.clean-fname) { display: flex; align-items: center; gap: 5px; max-width: 100%; min-width: 0; overflow: hidden; }
.clean-virtual-table :deep(.clean-fname-text) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.clean-virtual-table :deep(.clean-ficon-img) { width: 15px; height: 15px; flex-shrink: 0; }
.clean-virtual-table :deep(.clean-ficon) { font-size: 14px; flex-shrink: 0; }
.clean-virtual-table :deep(.clean-ficon-default) { color: #909399; }
.clean-virtual-table :deep(.clean-ficon-green) { color: #67c23a; }
.clean-virtual-table :deep(.clean-ficon-purple) { color: #9b59b6; }
.clean-virtual-table :deep(.clean-ficon-cyan) { color: #06b6d4; }
.clean-virtual-table :deep(.clean-ficon-yellow) { color: #e6a23c; }
.clean-virtual-table :deep(.clean-ficon-blue) { color: #409eff; }
.clean-virtual-table :deep(.clean-ficon-indigo) { color: #5c6bc0; }
.clean-virtual-table :deep(.clean-ficon-gray) { color: #909399; }
.clean-virtual-table :deep(.clean-ficon-red) { color: #f56c6c; }
.clean-recycle-toggle { display: flex; align-items: center; gap: 6px; cursor: pointer; user-select: none; }
.clean-open-recycle-btn { margin-left: auto; }
.clean-recycle-toggle:hover { color: #409eff; }
.clean-recycle-arrow { font-size: 10px; color: #909399; }
.clean-recycle-hint { font-size: 12px; color: #c0c4cc; }
.clean-summary { margin-bottom: 10px; font-size: 13px; color: #303133; }
.scan-pie-chart { margin-top: 8px; padding: 4px 0; }
.clean-trunc { margin-left: 6px; font-size: 12px; color: #909399; }
.clean-file-list { max-height: 320px; overflow-y: auto; padding: 6px 10px; border: 1px solid #ebeef5; border-radius: 6px; background: #fafafa; }
.clean-file-item { font-family: Consolas, monospace; font-size: 12px; line-height: 1.8; color: #606266; word-break: break-all; }
.clean-item-row { display: flex; align-items: center; gap: 8px; }
.clean-item-ok { color: #67c23a; flex-shrink: 0; }
.clean-item-fail { color: #f56c6c; flex-shrink: 0; }
.clean-item-path { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.clean-file-empty { font-size: 12px; color: #909399; }
.clean-result-header { display: flex; align-items: center; gap: 20px; margin-bottom: 16px; animation: cleanResultFadeIn .4s ease-out; }
@keyframes cleanResultFadeIn { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: translateY(0); } }
.clean-result-icon { flex-shrink: 0; width: 48px; height: 48px; border-radius: 50%; background: #f0f9eb; color: #67c23a; font-size: 24px; display: flex; align-items: center; justify-content: center; animation: cleanResultPulse .6s ease-out .3s both; }
@keyframes cleanResultPulse { 0% { transform: scale(0); opacity: 0; } 70% { transform: scale(1.15); } 100% { transform: scale(1); opacity: 1; } }
.clean-result-stats { display: flex; gap: 12px; flex-wrap: wrap; }
.clean-stat-card { flex: 1; min-width: 90px; padding: 10px 14px; background: #f5f7fa; border-radius: 8px; text-align: center; transition: transform .2s; }
.clean-stat-card:hover { transform: translateY(-2px); }
.clean-stat-num { display: block; font-size: 20px; font-weight: 700; color: #303133; margin-bottom: 4px; }
.clean-stat-label { font-size: 11px; color: #909399; }
</style>
