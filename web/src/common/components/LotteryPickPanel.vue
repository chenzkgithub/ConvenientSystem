<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { FullScreen, CopyDocument } from '@element-plus/icons-vue'
import { httpGet, httpPost, httpDelete } from '@/api/request'
import { fullscreenElement } from '@/common/utils/fullscreen'
import { formatDate, todayYmd } from '@/common/formatDate'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import {
  getLotteryConfig, isPositional, fmtNumber,
  type LotteryConfig, type LotteryZone,
} from '@/common/lottery'

/**
 * 选号界面独立组件：选号区 + 已选注数 + 数据库保存记录，支持浏览器全屏。
 * 供选号菜单页（LotteryView 选号页签）与选号记录页弹窗复用。
 */
const props = defineProps<{
  /** 彩种代码（DLT/SSQ/PL5/FC3D） */
  type: string
}>()

/** 一注彩票：前区号码 + 后区号码（位置型彩种后区为空） */
interface LotteryBet {
  front: number[]
  back: number[]
  /** 添加时间（ISO 字符串） */
  addedAt: string
}

/** 后端返回的带 Id 和时间戳的记录 */
interface LotteryRecord {
  id: number
  front: number[]
  back: number[]
  /** 所属期号（保存时默认取下一期；历史记录为空） */
  issueNumber: string | null
  /** 开奖日期（保存时默认取下一期开奖日；历史记录为空） */
  drawDate: string | null
  createdAt: string
}

const positional = computed(() => isPositional(props.type))

// ── 彩种配置 ──
const config = ref<LotteryConfig | null>(null)
const pickZones = computed<LotteryZone[]>(() => config.value?.pickZones ?? [])
const frontZones = computed(() => pickZones.value.filter(z => z.source === 'front'))
const backZones = computed(() => pickZones.value.filter(z => z.source === 'back'))
const frontLabel = computed(() => positional.value ? '号码' : (frontZones.value[0]?.label ?? '前区'))
const backLabel = computed(() => backZones.value[0]?.label ?? '后区')

/** 选号规则说明文本 */
const ruleText = computed(() => {
  if (!config.value) return ''
  if (positional.value) {
    return `共 ${pickZones.value.length} 位，每位选 1 个数字（0-9），顺序有意义`
  }
  const parts: string[] = []
  for (const z of pickZones.value) {
    const first = z.numbers[0]
    const last = z.numbers[z.numbers.length - 1]
    parts.push(`${z.label}选 ${z.pick} 个（${fmtNumber(false, first)}-${fmtNumber(false, last)}）`)
  }
  return parts.join(' + ')
})

// 选号状态：分区键 → 已选号码
const pickState = ref<Record<string, number[]>>({})
const betHistory = ref<LotteryBet[]>([])

// ── 时间工具（右面板分组用） ──
/** 截取到秒精度作为分组 key：yyyy-MM-dd HH:mm:ss */
function toSecondKey(iso: string): string {
  return iso.slice(0, 19).replace('T', ' ')
}

/** 右面板按添加时间（秒精度）分组 */
const groupedHistory = computed(() => {
  const groups: { key: string; label: string; items: { bet: LotteryBet; globalIdx: number }[] }[] = []
  const map = new Map<string, { bet: LotteryBet; globalIdx: number }[]>()
  betHistory.value.forEach((bet, idx) => {
    const key = toSecondKey(bet.addedAt)
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push({ bet, globalIdx: idx })
  })
  map.forEach((items, key) => groups.push({ key, label: key, items }))
  return groups
})

// ── 数据库保存记录（分页） ──
const savedRecords = ref<LotteryRecord[]>([])
const savedTotal = ref(0)
const savedPage = ref(1)
const savedSize = ref(20)
const savedLoading = ref(false)

// 筛选口径为开奖日期，默认选中今天（可清空）
const filterDate = ref(todayYmd())

// ── 选号状态计算 ──
function zoneCount(key: string): number { return (pickState.value[key] ?? []).length }
function isZoneFull(z: LotteryZone): boolean { return zoneCount(z.key) === z.pick }
const isComplete = computed(() => pickZones.value.length > 0 && pickZones.value.every(isZoneFull))

/** 号码显示文本 */
function fmt(n: number): string { return fmtNumber(positional.value, n) }

// ── 保存记录表格列配置（随彩种动态生成） ──
const savedColumns = computed<DataTableColumn<LotteryRecord>[]>(() => {
  const cols: DataTableColumn<LotteryRecord>[] = [
    { type: 'index', label: '#', width: 50, align: 'center' },
    {
      prop: 'issueNumber', label: '期号', width: 90, align: 'center',
      formatter: (row) => row.issueNumber ?? '—',
    },
    {
      prop: 'drawDate', label: '开奖日期', width: 110, align: 'center', className: 'cell-nowrap',
      formatter: (row) => row.drawDate ? formatDate(row.drawDate).slice(0, 10) : '—',
    },
    {
      prop: 'createdAt', label: '选号时间', width: 170, className: 'cell-nowrap',
      formatter: (row) => formatDate(row.createdAt),
    },
    {
      prop: 'front', label: frontLabel.value,
      minWidth: positional.value ? 150 : 180,
      custom: true,
    },
  ]
  if (backZones.value.length > 0) {
    cols.push({ prop: 'back', label: backLabel.value, width: 80, custom: true })
  }
  return cols
})

// ── 加载保存记录 ──
async function loadSaved() {
  savedLoading.value = true
  try {
    const params: Record<string, unknown> = { type: props.type, page: savedPage.value, size: savedSize.value }
    if (filterDate.value) params.date = filterDate.value
    const res = await httpGet<{ total: number; list: LotteryRecord[] }>('/api/Common/Lottery/List', params)
    savedRecords.value = res.list
    savedTotal.value = res.total
  } catch {
    savedRecords.value = []
    savedTotal.value = 0
  } finally {
    savedLoading.value = false
  }
}

// ── 选号操作 ──
function toggleZone(zone: LotteryZone, n: number) {
  const arr = [...(pickState.value[zone.key] ?? [])]
  const idx = arr.indexOf(n)
  if (zone.positional) {
    // 位置型每位只留 1 个数字：点击切换
    pickState.value[zone.key] = idx >= 0 ? [] : [n]
    return
  }
  if (idx >= 0) {
    arr.splice(idx, 1)
  } else if (arr.length < zone.pick) {
    arr.push(n)
  } else {
    ElMessage.warning({ message: `${zone.label}最多选 ${zone.pick} 个号码`, appendTo: fullscreenElement() })
    return
  }
  pickState.value[zone.key] = arr
}

/** 随机生成一注各分区的号码 */
function randomZonePicks(): Record<string, number[]> {
  const picks: Record<string, number[]> = {}
  for (const z of pickZones.value) {
    if (z.positional) {
      picks[z.key] = [Math.floor(Math.random() * z.numbers.length)]
    } else {
      picks[z.key] = shuffle([...z.numbers]).slice(0, z.pick).sort((a, b) => a - b)
    }
  }
  return picks
}

/** 把分区选号组装成一注：位置型按 posIndex 归位，池选型升序 */
function buildBet(picks: Record<string, number[]>): LotteryBet {
  const front: number[] = []
  const back: number[] = []
  for (const z of pickZones.value) {
    const nums = picks[z.key] ?? []
    if (positional.value && z.source === 'front') {
      front[z.posIndex] = nums[0] ?? 0
    } else if (z.source === 'front') {
      front.push(...nums)
    } else {
      back.push(...nums)
    }
  }
  if (!positional.value) {
    front.sort((a, b) => a - b)
    back.sort((a, b) => a - b)
  }
  return { front, back, addedAt: new Date().toISOString() }
}

/** 机选一注 */
function randomPick() {
  const picks = randomZonePicks()
  pickState.value = picks
}

/** 清空当前选号 */
function clearPick() {
  pickState.value = {}
}

function isSameBet(a: LotteryBet, b: LotteryBet): boolean {
  return a.front.join(',') === b.front.join(',') && a.back.join(',') === b.back.join(',')
}

/** 确认当前注，加入历史 */
function confirmBet() {
  if (!isComplete.value) {
    ElMessage.warning({ message: `请先选满全部号码：${ruleText.value}`, appendTo: fullscreenElement() })
    return
  }
  const bet = buildBet(pickState.value)
  if (betHistory.value.some(h => isSameBet(h, bet))) {
    ElMessage.warning({ message: '该注已添加过', appendTo: fullscreenElement() })
    return
  }
  betHistory.value.push(bet)
  ElMessage.success({ message: '已添加一注', appendTo: fullscreenElement() })
  clearPick()
}

/** 机选 N 注 */
function randomMulti(count: number) {
  for (let i = 0; i < count; i++) {
    const bet = buildBet(randomZonePicks())
    if (!betHistory.value.some(h => isSameBet(h, bet))) betHistory.value.push(bet)
  }
  ElMessage.success({ message: `已机选 ${count} 注`, appendTo: fullscreenElement() })
}

/** 删除历史中的一注 */
function removeBet(index: number) {
  betHistory.value.splice(index, 1)
}

/** 清空所有历史 */
function clearHistory() {
  betHistory.value = []
}

/** 保存当前所有注到数据库 */
async function saveToDb() {
  if (betHistory.value.length === 0) {
    ElMessage.warning({ message: '暂无可保存的注数', appendTo: fullscreenElement() })
    return
  }
  try {
    await httpPost<LotteryRecord[]>('/api/Common/Lottery/Save', {
      type: props.type,
      bets: betHistory.value.map(b => ({ front: b.front, back: b.back })),
    })
    ElMessage.success({ message: `已保存 ${betHistory.value.length} 注到数据库`, appendTo: fullscreenElement() })
    betHistory.value = []
    loadSaved()
  } catch { /* httpPost 内部已弹错误 */ }
}

/** 删除数据库中的一条记录 */
async function removeSaved(id: number) {
  try {
    await httpDelete<boolean>(`/api/Common/Lottery/Delete?id=${id}`)
    loadSaved()
  } catch { /* 内部已弹错误 */ }
}

/** 删除筛选日期全部记录 */
function removeFilteredDate() {
  if (!filterDate.value) return
  // ElMessageBox 挂载在 body 且不支持指定容器：全屏时先退出全屏，保证确认框可见
  if (document.fullscreenElement) void document.exitFullscreen()
  ElMessageBox.confirm(`确定删除开奖日期为 ${filterDate.value} 的全部记录吗？`, '确认删除', {
    confirmButtonText: '删除', cancelButtonText: '取消',
    type: 'warning', confirmButtonClass: 'el-button--danger',
  }).then(async () => {
    try {
      const n = await httpDelete<number>(`/api/Common/Lottery/DeleteByDate?type=${props.type}&date=${filterDate.value}`)
      ElMessage.success({ message: `已删除 ${n} 条`, appendTo: fullscreenElement() })
      savedPage.value = 1
      loadSaved()
    } catch { /* 内部已弹错误 */ }
  }).catch(() => {})
}

function querySaved() {
  savedPage.value = 1
  loadSaved()
}

/** Fisher-Yates 洗牌 */
function shuffle<T>(arr: T[]): T[] {
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]]
  }
  return arr
}

/** 格式化一注为文本 */
function formatBet(bet: LotteryBet): string {
  const f = bet.front.map(fmt).join(' ')
  if (bet.back.length === 0) return f
  const b = bet.back.map(fmt).join(' ')
  return `${f} + ${b}`
}

/** 导出所有注为文本 */
const exportText = computed(() => {
  if (betHistory.value.length === 0) return ''
  return betHistory.value
    .map((bet, i) => `第${i + 1}注：${formatBet(bet)}`)
    .join('\n')
})

function copyExport() {
  if (!exportText.value) return
  navigator.clipboard.writeText(exportText.value).then(() => {
    ElMessage.success({ message: '已复制到剪贴板', appendTo: fullscreenElement() })
  }).catch(() => {
    ElMessage.error({ message: '复制失败', appendTo: fullscreenElement() })
  })
}

// ── 初始化：加载彩种配置 + 保存记录 ──
async function loadConfig() {
  try {
    config.value = await getLotteryConfig(props.type)
  } catch {
    config.value = null
  }
}

onMounted(() => {
  loadConfig()
  loadSaved()
})

// 彩种切换（父组件换 type）时重置选号状态/历史注/分页并重新加载
watch(() => props.type, () => {
  pickState.value = {}
  betHistory.value = []
  filterDate.value = ''
  savedPage.value = 1
  loadConfig()
  loadSaved()
})

// ── 全屏展示（浏览器 Fullscreen API，作用于选号容器） ──
const pickContainerRef = ref<HTMLElement>()
const isFullscreen = ref(false)

async function toggleFullscreen() {
  try {
    if (document.fullscreenElement) {
      await document.exitFullscreen()
    } else if (pickContainerRef.value) {
      await pickContainerRef.value.requestFullscreen()
    }
  } catch {
    ElMessage.warning({ message: '当前浏览器不支持全屏', appendTo: fullscreenElement() })
  }
}

function onFullscreenChange() {
  isFullscreen.value = !!document.fullscreenElement
}
onMounted(() => document.addEventListener('fullscreenchange', onFullscreenChange))
onUnmounted(() => document.removeEventListener('fullscreenchange', onFullscreenChange))
</script>

<template>
  <div class="pick-container" ref="pickContainerRef">
    <div class="pick-header">
      <h2>{{ config?.name ?? '' }}选号</h2>
      <span class="pick-rule">{{ ruleText }}</span>
      <CommonTooltip
        :content="isFullscreen ? '退出全屏' : '全屏展示'"
        :copyable="false"
        :teleported="!isFullscreen"
      >
        <el-button class="pick-fullscreen-btn" :icon="isFullscreen ? CopyDocument : FullScreen" @click="toggleFullscreen" />
      </CommonTooltip>
    </div>

    <div class="pick-body">
      <!-- 左列：选号区 + 保存记录 -->
      <div class="left-col">
        <!-- 选号区 -->
        <div class="pick-panel">
          <div v-for="zone in pickZones" :key="zone.key" class="zone">
            <div class="zone-title">
              <span class="zone-label" :class="zone.source === 'back' ? 'back-label' : 'front-label'">{{ zone.label }}</span>
              <span class="zone-count">已选 <em :class="{ ok: isZoneFull(zone) }">{{ zoneCount(zone.key) }}</em> / {{ zone.pick }}</span>
            </div>
            <div class="number-grid" :class="zone.source === 'back' ? 'back-grid' : 'front-grid'">
              <button v-for="n in zone.numbers" :key="zone.key + '-' + n"
                class="num-ball" :class="[zone.source === 'back' ? 'back-ball' : 'front-ball', { selected: (pickState[zone.key] ?? []).includes(n) }]"
                @click="toggleZone(zone, n)">
                {{ fmtNumber(zone.positional, n) }}
              </button>
            </div>
          </div>
          <!-- 操作栏 -->
          <div class="action-bar">
            <el-button type="primary" @click="randomPick">机选一注</el-button>
            <el-button @click="clearPick">清空选号</el-button>
            <el-button type="success" :disabled="!isComplete" @click="confirmBet">添加该注</el-button>
          </div>
          <!-- 当前选号预览 -->
          <div class="current-pick" v-if="pickZones.some(z => zoneCount(z.key) > 0)">
            <span class="pick-label">当前：</span>
            <template v-for="zone in pickZones" :key="'pv' + zone.key">
              <span v-if="zoneCount(zone.key) > 0" class="pick-zone">
                <span v-for="n in [...(pickState[zone.key] ?? [])].sort((a, b) => a - b)" :key="'pv' + zone.key + n"
                  class="mini-ball" :class="zone.source === 'back' ? 'back-mini' : 'front-mini'">{{ fmtNumber(zone.positional, n) }}</span>
              </span>
            </template>
          </div>
        </div>

        <!-- 左下角：数据库保存记录（分页表格） -->
        <div class="saved-panel">
          <CommonDataTable
            v-model:page="savedPage"
            v-model:pageSize="savedSize"
            :columns="savedColumns"
            :data="savedRecords"
            :loading="savedLoading"
            :total="savedTotal"
            :actions-width="70"
            :page-sizes="[10, 20, 50]"
            :teleported="!isFullscreen"
            compact
            pagination-layout="total, sizes, prev, pager, next"
            @load="loadSaved"
          >
            <template #filters>
              <el-date-picker
                v-model="filterDate"
                type="date"
                placeholder="按开奖日期筛选"
                value-format="YYYY-MM-DD"
                style="width: 160px"
                clearable
                :teleported="!isFullscreen"
                @change="querySaved"
              />
            </template>

            <template #toolbar>
              <el-button type="primary" size="small" @click="querySaved">查询</el-button>
              <el-button v-if="filterDate" type="danger" size="small" plain @click="removeFilteredDate">删除该日</el-button>
              <el-button size="small" @click="loadSaved">刷新</el-button>
            </template>

            <template #cell-front="{ row }">
              <div class="balls-cell">
                <span v-for="(n, i) in row.front" :key="'sf' + i" class="mini-ball front-mini">{{ fmt(n) }}</span>
              </div>
            </template>

            <template #cell-back="{ row }">
              <div class="balls-cell">
                <span v-for="(n, i) in row.back" :key="'sb' + i" class="mini-ball back-mini">{{ fmt(n) }}</span>
              </div>
            </template>

            <template #actions="{ row }">
              <el-button link type="danger" size="small" @click="removeSaved((row as LotteryRecord).id)">删除</el-button>
            </template>

            <template #empty>暂无保存记录</template>
          </CommonDataTable>
        </div>
      </div>

      <!-- 右面板：已选注数 -->
      <div class="history-panel">
        <div class="history-header">
          <h3>已选<span class="bet-count">({{ betHistory.length }})</span></h3>
          <div class="history-actions">
            <el-button size="small" @click="randomMulti(5)">机选 5 注</el-button>
            <el-button size="small" @click="randomMulti(10)">机选 10 注</el-button>
            <el-button size="small" type="warning" plain :disabled="betHistory.length === 0" @click="saveToDb">保存</el-button>
            <el-button size="small" type="danger" plain :disabled="betHistory.length === 0" @click="clearHistory">清空</el-button>
          </div>
        </div>

        <div class="history-list" v-if="groupedHistory.length > 0">
          <div v-for="group in groupedHistory" :key="group.key" class="date-group">
            <div class="date-group-header">
              <span class="date-group-label">{{ group.label }}</span>
              <span class="date-group-stat">{{ group.items.length }} 注</span>
            </div>
            <div v-for="{ bet, globalIdx } in group.items" :key="globalIdx" class="bet-row">
              <div class="bet-balls">
                <span v-for="(n, i) in bet.front" :key="'hf' + i" class="mini-ball front-mini">{{ fmt(n) }}</span>
                <template v-if="bet.back.length > 0">
                  <span class="bet-sep">+</span>
                  <span v-for="(n, i) in bet.back" :key="'hb' + i" class="mini-ball back-mini">{{ fmt(n) }}</span>
                </template>
              </div>
              <button class="bet-remove" title="删除" @click="removeBet(globalIdx)">&times;</button>
            </div>
          </div>
        </div>
        <div v-else class="history-empty">
          <el-empty description="暂无选号，请在左侧选号或点击机选" :image-size="80" />
        </div>

        <!-- 导出区 -->
        <div class="export-area" v-if="betHistory.length > 0">
          <div class="export-bar">
            <span class="export-summary">共 {{ betHistory.length }} 注，合计 {{ betHistory.length * 2 }} 元</span>
            <el-button size="small" type="primary" plain @click="copyExport">复制文本</el-button>
          </div>
          <pre class="export-text">{{ exportText }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 日期/时间列内容不换行，保证完整展示 */
:deep(.cell-nowrap .cell) {
  white-space: nowrap;
}

/* 选号容器：撑满父级（菜单页页签 / 全屏弹窗均可） */
.pick-container {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: #fff;
}
.pick-container:fullscreen {
  background: #fff;
  width: 100%;
  height: 100%;
  padding: 12px 16px;
}

.pick-header {
  display: flex;
  align-items: baseline;
  gap: 16px;
  margin-bottom: 16px;
  flex-shrink: 0;
}
.pick-header h2 { margin: 0; font-size: 18px; font-weight: 600; color: var(--text-primary, #303133); }
.pick-rule { font-size: 13px; color: #909399; }

/* 全屏按钮靠右 */
.pick-fullscreen-btn { margin-left: auto; align-self: center; }

.pick-body {
  flex: 1;
  display: flex;
  gap: 16px;
  overflow: hidden;
}

/* ── 左列：选号 + 保存记录 ── */
.left-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 12px;
  overflow: hidden;
  min-width: 0;
}

/* ── 选号区 ── */
.pick-panel {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.zone-title {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}
.zone-label {
  font-size: 14px; font-weight: 600; padding: 2px 10px;
  border-radius: 4px; color: #fff;
}
.front-label { background: #e6393a; }
.back-label { background: #2563eb; }
.zone-count { font-size: 13px; color: #606266; }
.zone-count em { font-style: normal; font-weight: 600; color: #e6393a; }
.zone-count em.ok { color: #67c23a; }

.number-grid { display: flex; flex-wrap: wrap; gap: 8px; }

.num-ball {
  width: 40px; height: 40px; border-radius: 50%;
  border: 2px solid #dcdfe6; background: #fff;
  font-size: 14px; font-weight: 600; color: #606266;
  cursor: pointer; transition: all 0.15s;
  display: flex; align-items: center; justify-content: center;
}
.num-ball:hover { border-color: #c0c4cc; transform: scale(1.08); }

.front-ball.selected {
  background: #e6393a; border-color: #e6393a; color: #fff;
  box-shadow: 0 2px 8px rgba(230, 57, 58, 0.35); transform: scale(1.1);
}
.back-ball.selected {
  background: #2563eb; border-color: #2563eb; color: #fff;
  box-shadow: 0 2px 8px rgba(37, 99, 235, 0.35); transform: scale(1.1);
}

.action-bar { display: flex; gap: 8px; flex-shrink: 0; }

.current-pick {
  display: flex; align-items: center; gap: 6px;
  padding: 10px 12px; background: #f5f7fa; border-radius: 8px; flex-wrap: wrap;
}
.pick-label { font-size: 13px; color: #909399; }
.pick-zone { display: inline-flex; gap: 4px; margin-right: 6px; }

.mini-ball {
  display: inline-flex; align-items: center; justify-content: center;
  width: 28px; height: 28px; border-radius: 50%;
  font-size: 12px; font-weight: 700; color: #fff; flex-shrink: 0;
}
.front-mini { background: #e6393a; }
.back-mini { background: #2563eb; }

.balls-cell {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: nowrap;
}

/* ── 左下角：保存记录表格 ── */
.saved-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  border: 1px solid #e4e7ed;
  border-radius: 8px;
  overflow: hidden;
  background: #fafbfc;
  min-height: 0;
}

/* ── 右面板：已选注数 ── */
.history-panel {
  width: 400px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  border: 1px solid #e4e7ed;
  border-radius: 8px;
  overflow: hidden;
  background: #fafbfc;
}

.history-header {
  padding: 12px 14px;
  border-bottom: 1px solid #e4e7ed;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
  flex-wrap: nowrap;
  gap: 8px;
}
.history-header h3 { margin: 0; font-size: 14px; font-weight: 600; color: #303133; white-space: nowrap; flex-shrink: 0; }
.bet-count {
  font-size: 13px; font-weight: 500; color: #606266;
}
.history-actions { display: flex; gap: 4px; flex-wrap: nowrap; flex-shrink: 0; }

.history-list { flex: 1; overflow-y: auto; padding: 8px; }

.date-group { margin-bottom: 10px; }
.date-group-header {
  display: flex; align-items: center; gap: 6px;
  padding: 5px 8px; background: #f0f2f5; border-radius: 6px; margin-bottom: 4px;
}
.date-group-label { font-size: 13px; font-weight: 600; color: #303133; }
.date-group-date { font-size: 11px; color: #909399; }
.date-group-stat { font-size: 11px; color: #606266; margin-left: auto; }

.bet-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  margin-bottom: 4px;
  background: #fff;
  border: 1px solid #ebeef5;
  transition: background 0.15s;
  overflow: hidden;
}
.bet-row:hover { background: #f5f7fa; }

.bet-balls {
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 1;
  flex-wrap: nowrap;
  overflow: hidden;
}
.bet-sep { color: #c0c4cc; font-size: 13px; margin: 0 2px; flex-shrink: 0; }

.bet-remove {
  width: 22px; height: 22px; border: none; background: transparent;
  color: #c0c4cc; font-size: 18px; cursor: pointer; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0; transition: all 0.15s;
}
.bet-remove:hover { background: #fef0f0; color: #e6393a; }

.history-empty { flex: 1; display: flex; align-items: center; justify-content: center; }

/* ── 导出区 ── */
.export-area { border-top: 1px solid #e4e7ed; padding: 10px 14px; flex-shrink: 0; }
.export-bar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; }
.export-summary { font-size: 13px; color: #606266; font-weight: 500; }
.export-text {
  margin: 0; padding: 8px 10px; background: #fff;
  border: 1px solid #e4e7ed; border-radius: 6px;
  font-size: 12px; line-height: 1.8; color: #303133;
  max-height: 120px; overflow-y: auto; white-space: pre-wrap;
  font-family: 'Consolas', 'Monaco', monospace;
}
</style>
