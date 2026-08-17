<template>
  <div class="trend-container" ref="containerRef">
    <!-- 顶部控制栏 -->
    <div class="trend-toolbar">
      <div class="toolbar-left">
        <span class="toolbar-label">统计期数：</span>
        <el-radio-group v-model="periods" :disabled="!!dateRange || isMatchMode" @change="loadTrend">
          <el-radio-button :value="50">50 期</el-radio-button>
          <el-radio-button :value="100">100 期</el-radio-button>
          <el-radio-button :value="200">200 期</el-radio-button>
          <el-radio-button :value="500">500 期</el-radio-button>
        </el-radio-group>
        <span class="toolbar-label">开奖日期：</span>
        <el-date-picker v-model="dateRange" type="daterange" value-format="YYYY-MM-DD"
          range-separator="至" start-placeholder="开始日期" end-placeholder="结束日期"
          clearable class="toolbar-daterange" :disabled="isMatchMode"
          :teleported="!isFullscreen" @change="loadTrend" />
        <span class="toolbar-sep"></span>
        <!-- 历史号码匹配：弹窗点选号码，全库检索同时满足全部条件的期并按期号降序展示（此时期数与日期不参与） -->
        <el-button type="primary" @click="matchDialogRef?.open(activeMatch)">历史匹配</el-button>
        <!-- 也在 activeMatch 已设但请求失败（尚未进入匹配模式）时显示，否则参数清不掉 -->
        <el-button v-if="isMatchMode || activeMatch" @click="clearMatch">退出匹配</el-button>
        <span v-if="isMatchMode" class="toolbar-match-tip">{{ matchTip }}</span>
      </div>
      <div class="toolbar-right">
        <span class="toolbar-hint">双击开奖行可查看官网通告</span>
        <CommonTooltip :content="isFullscreen ? '退出全屏' : '全屏展示'" :copyable="false" :teleported="!isFullscreen">
          <el-button :icon="isFullscreen ? CopyDocument : FullScreen" @click="toggleFullscreen" />
        </CommonTooltip>
        <el-button @click="ruleDialogRef?.open(type)">玩法规则</el-button>
        <el-button @click="loadTrend">刷新</el-button>
      </div>
    </div>

    <!-- 走势图主体 -->
    <div class="trend-content" v-loading="loading">
      <div class="trend-scroll" v-if="trendData">
        <div class="trend-section">
          <div class="trend-table-wrapper" ref="tableWrapperRef">
            <table class="trend-table">
              <thead>
                <!-- 分区标题行 -->
                <tr class="zone-header-row">
                  <th class="sticky-col issue-cell" rowspan="2">期号</th>
                  <th class="sticky-col date-cell" rowspan="2">日期</th>
                  <th class="sticky-col week-cell" rowspan="2">星期</th>
                  <th v-for="(g, gi) in groups" :key="'zh' + g.key"
                    :colspan="g.numbers.length" class="zone-header" :class="colorClass(gi)">
                    {{ g.label }}
                  </th>
                  <!-- 右侧统计列（前区号码维度汇总） -->
                  <template v-if="showSummaryCols">
                    <th v-for="(c, ci) in summaryCols" :key="'sh' + c.key" rowspan="2"
                      class="summary-header" :class="ci === 0 ? 'summary-start' : ''">
                      {{ c.label }}
                    </th>
                  </template>
                </tr>
                <!-- 号码行 -->
                <tr>
                  <template v-for="(g, gi) in groups" :key="'hn' + g.key">
                    <th v-for="(n, ni) in g.numbers" :key="'h' + g.key + n" class="num-header"
                      :class="[colorClass(gi), ni === 0 && gi > 0 ? 'zone-start' : '']">
                      {{ fmt(g, n) }}
                    </th>
                  </template>
                </tr>
              </thead>
              <tbody>
                <tr v-for="draw in trendData.draws" :key="draw.issueNumber"
                  :class="{ 'selected-row': selectedIssue === draw.issueNumber }"
                  @click="selectedIssue = selectedIssue === draw.issueNumber ? '' : draw.issueNumber"
                  @dblclick="noticeDialogRef?.open(type, draw.issueNumber)">
                  <td class="sticky-col issue-cell">{{ draw.issueNumber }}</td>
                  <td class="sticky-col date-cell">{{ draw.drawDate?.substring(0, 10) }}</td>
                  <td class="sticky-col week-cell">{{ getWeekDay(draw.drawDate) }}</td>
                  <template v-for="(g, gi) in groups" :key="'d' + draw.issueNumber + g.key">
                    <td v-for="(n, ni) in g.numbers" :key="'c' + g.key + n"
                      :data-line="isHit(draw, g, n) && lineZoneKeys.has(g.key) ? g.key : undefined"
                      :class="['num-cell', colorClass(gi), ni === 0 && gi > 0 ? 'zone-start' : '',
                        isZoneEmpty(draw, g) ? 'zone-empty' : '',
                        isHit(draw, g, n) ? hitClass(draw, g, n) : 'miss']">
                      <!-- 命中号码圆球展示（前区红/后区蓝、连号空心、组选区空心小圈）；未命中显示与上次出现该号码的间隔期数（遗漏值，淡灰） -->
                      <span v-if="isHit(draw, g, n)">{{ fmt(g, n) }}</span>
                      <template v-else>{{ missOf(draw, g, n) }}</template>
                    </td>
                  </template>
                  <!-- 右侧统计列：各类比值偏态时高亮 -->
                  <template v-if="showSummaryCols">
                    <td v-for="(c, ci) in summaryCols" :key="'sc' + c.key"
                      class="summary-cell" :class="ci === 0 ? 'summary-start' : ''">
                      <span :class="c.extreme && c.extreme(draw) ? c.extremeClass : ''">{{ c.value(draw) }}</span>
                    </td>
                  </template>
                </tr>
              </tbody>
              <tfoot>
                <!-- 预选行 -->
                <tr v-for="pred in predictions" :key="'pred' + pred.id" class="prediction-row">
                  <td class="sticky-col prediction-label" colspan="3">
                    {{ pred.name }}
                    <span class="prediction-remove" @click.stop="removePrediction(pred)">×</span>
                  </td>
                  <template v-for="(g, gi) in groups" :key="'p' + pred.id + g.key">
                    <td v-for="(n, ni) in g.numbers" :key="'pc' + g.key + n"
                      :class="['prediction-cell', colorClass(gi), ni === 0 && gi > 0 ? 'zone-start' : '',
                        isPredicted(pred, g, n) ? 'selected' : '']"
                      @click.stop="togglePrediction(pred, g, n)">
                      <span v-if="isPredicted(pred, g, n)">{{ fmt(g, n) }}</span>
                      <template v-else>{{ fmt(g, n) }}</template>
                    </td>
                  </template>
                  <!-- 预选行右侧统计列占位 -->
                  <template v-if="showSummaryCols">
                    <td v-for="(c, ci) in summaryCols" :key="'ps' + c.key"
                      class="prediction-cell" :class="ci === 0 ? 'summary-start' : ''"></td>
                  </template>
                </tr>
                <!-- 预选操作行（公开模式下隐藏保存按钮） -->
                <tr class="prediction-action-row">
                  <td class="sticky-col prediction-action-label" colspan="3">
                    <el-button size="small" @click="addPrediction">+ 添加预选</el-button>
                    <el-button v-if="!publicMode" size="small" type="primary" @click="savePredictions">保存选号</el-button>
                  </td>
                  <td :colspan="totalCols + summaryColCount"></td>
                </tr>
                <tr v-for="(row, ri) in visibleStatRows" :key="row.key" class="stat-row">
                  <td class="sticky-col stat-label" colspan="3" :style="statRowStyle(ri)">{{ row.label }}</td>
                  <template v-for="(g, gi) in groups" :key="row.key + g.key">
                    <td v-for="(s, si) in (g.stats || [])" :key="row.key + g.key + s.number"
                      class="stat-cell" :style="statRowStyle(ri)"
                      :class="[row.miss ? 'miss-cell' : '', colorClass(gi), si === 0 && gi > 0 ? 'zone-start' : '']">
                      {{ (s as any)[row.field] }}
                    </td>
                  </template>
                  <!-- 统计行右侧统计列占位 -->
                  <template v-if="showSummaryCols">
                    <td v-for="(c, ci) in summaryCols" :key="'ss' + c.key" class="stat-cell"
                      :class="ci === 0 ? 'summary-start' : ''" :style="statRowStyle(ri)"></td>
                  </template>
                </tr>
              </tfoot>
            </table>
            <!-- 号码连线：覆盖在表格上的 SVG，层级低于固定列与表头，滚动时自然被冻结区域遮挡 -->
            <svg v-if="trendLines.length" class="trend-lines"
              :width="lineCanvas.w" :height="lineCanvas.h">
              <polyline v-for="l in trendLines" :key="'ln' + l.key" :points="l.points"
                :class="l.back ? 'line-back' : 'line-front'" />
            </svg>
          </div>
        </div>
      </div>
      <el-empty v-else-if="!loading" description="暂无开奖数据，将由定时任务自动更新" />
    </div>

    <!-- 官网通告 · 全国中奖情况弹窗（双击某期行触发，共享组件） -->
    <LotteryNoticeDialog ref="noticeDialogRef" />

    <!-- 玩法规则 · 奖级对照表弹窗（工具栏按钮触发，共享组件） -->
    <LotteryRuleDialog ref="ruleDialogRef" />

    <!-- 历史号码匹配弹窗（工具栏按钮触发：点选号码后全库检索） -->
    <LotteryMatchDialog ref="matchDialogRef" :pick-zones="pickZones" @confirm="onMatchConfirm" />

  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { httpPost } from '@/api/request'
import { fullscreenElement } from '@/common/utils/fullscreen'
import { ElMessage } from 'element-plus'
import { FullScreen, CopyDocument } from '@element-plus/icons-vue'
import LotteryNoticeDialog from '@/common/components/LotteryNoticeDialog.vue'
import LotteryRuleDialog from '@/common/components/LotteryRuleDialog.vue'
import LotteryMatchDialog from '@/common/components/LotteryMatchDialog.vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import {
  getLotteryTrend, fmtNumber,
  type TrendData, type LotteryZone, type LotteryDraw, type LotteryMatchSpec,
} from '@/common/lottery'

const props = defineProps<{
  /** 彩种代码（DLT/SSQ/PL5/FC3D） */
  type: string
  /** 选号分区（来自彩种配置，预选保存时用于校验与组装注数） */
  pickZones: LotteryZone[]
  /** 公开模式：隐藏保存选号按钮（外部链接访问时启用） */
  publicMode?: boolean
}>()

// ── 走势图数据 ──
const loading = ref(false)
const periods = ref(50)
/** 开奖日期区间（yyyy-MM-dd，选中时优先于统计期数） */
const dateRange = ref<[string, string] | null>(null)
const trendData = ref<TrendData | null>(null)
const selectedIssue = ref('')
/** 表格滚动容器（用于测算左侧固定列实际宽度） */
const tableWrapperRef = ref<HTMLElement>()

/** 走势分区（顺序即列顺序） */
const groups = computed<LotteryZone[]>(() => trendData.value?.groups ?? [])

/** 号码列总数（操作行 colspan 用） */
const totalCols = computed(() => groups.value.reduce((s, g) => s + g.numbers.length, 0))

/** 全部统计行定义 */
const ALL_STAT_ROWS = [
  { key: 'count', label: '出现次数', field: 'count', miss: false },
  { key: 'currentMiss', label: '当前遗漏', field: 'currentMiss', miss: true },
  { key: 'avgMiss', label: '平均遗漏', field: 'avgMiss', miss: false },
  { key: 'maxMiss', label: '最大遗漏', field: 'maxMiss', miss: true },
  { key: 'maxConsecutive', label: '最大连出', field: 'maxConsecutive', miss: false },
]

/**
 * 实际展示的统计行：匹配模式下只保留命中期、期与期不再相邻，
 * 遗漏与连出都是按“相邻期”算的，故只保留仍成立的出现次数（即匹配结果内的出现期数）。
 */
const visibleStatRows = computed(() =>
  isMatchMode.value ? ALL_STAT_ROWS.filter(r => r.key === 'count') : ALL_STAT_ROWS)

/** 统计行行高（与 CSS 中 tfoot .stat-row td 的 height 保持一致） */
const STAT_ROW_HEIGHT = 24

/**
 * 统计行粘底偏移：多行均需固定在底部，若全部 bottom:0 会重叠堆叠成一行，
 * 导致其余行位置露出下方 tbody 的号码球，因此按行序逆向叠加行高。
 */
function statRowStyle(rowIndex: number) {
  return { bottom: `${(visibleStatRows.value.length - 1 - rowIndex) * STAT_ROW_HEIGHT}px` }
}

// ── 历史号码匹配（工具栏按钮打开弹窗点选号码） ──
/** 已生效的匹配条件：弹窗点“开始匹配”时才写入，避免边选边请求 */
const activeMatch = ref<LotteryMatchSpec | null>(null)

/** 匹配弹窗（点选号码后回调 onMatchConfirm） */
const matchDialogRef = ref<InstanceType<typeof LotteryMatchDialog>>()

/** 是否处于匹配模式（以后端返回为准，确保与当前展示的数据一致） */
const isMatchMode = computed(() => !!trendData.value?.matchMode)

/** 当前彩种是否位置型（排列五/福彩3D，前区为各位 0-9 数字，回显不补零） */
const isPositionalType = computed(() => props.pickZones.some(z => z.positional))

/**
 * 已匹配条件回显：号码录入已移入弹窗，工具栏仍需看得到当前匹配什么。
 * 数位条件带上位名（如“万位5 个位3,7”），因为同一数字在不同位上含义不同，只列数字看不出条件。
 */
const matchNumsText = computed(() => {
  const m = activeMatch.value
  if (!m) return ''
  const posZones = props.pickZones.filter(z => z.positional)
  const p = m.pos
    .map((digits, i) => (digits.length > 0
      ? `${posZones.find(z => z.posIndex === i)?.label ?? `第${i + 1}位`}${digits.join(',')}`
      : ''))
    .filter(s => s)
    .join(' ')
  const f = m.front.map(n => fmtNumber(isPositionalType.value, n)).join(' ')
  const b = m.back.map(n => fmtNumber(false, n)).join(' ')
  return [p, f, b].filter(s => s).join(' + ')
})

/** 匹配结果提示：匹配条件与命中总期数，超出展示上限时说明已截断 */
const matchTip = computed(() => {
  const d = trendData.value
  if (!d?.matchMode) return ''
  const shown = d.draws.length
  return d.matchTotal > shown
    ? `${matchNumsText.value}：共匹配 ${d.matchTotal} 期，展示最近 ${shown} 期`
    : `${matchNumsText.value}：共匹配 ${shown} 期`
})

/** 弹窗确定：条件取自本彩种号码池点选，无需再校验，直接生效并重拉数据 */
function onMatchConfirm(spec: LotteryMatchSpec) {
  activeMatch.value = spec
  loadTrend()
}

/** 退出匹配，回到按期数/日期的常规走势图 */
function clearMatch() {
  if (!activeMatch.value) return
  activeMatch.value = null
  loadTrend()
}

// ── 渲染辅助 ──
/** 分区着色：后区/蓝球用 zone-back，其余前区分区按序循环三色 */
function colorClass(gi: number): string {
  const g = groups.value[gi]
  if (!g) return ''
  if (g.source === 'back') return 'zone-back'
  const frontIdx = groups.value.slice(0, gi).filter(x => x.source !== 'back').length
  return `zone-${(frontIdx % 3) + 1}`
}

/** 号码显示文本：单数字号码池（位置型各位、福彩3D 组选分布区的 0-9）不补零，两位号码池补零两位 */
function fmt(g: LotteryZone, n: number): string {
  return fmtNumber(g.positional || g.numbers.every(x => x < 10), n)
}

/** 某期某分区某号码是否命中 */
function isHit(draw: LotteryDraw, g: LotteryZone, n: number): boolean {
  if (g.positional) return draw.front[g.posIndex] === n
  return g.source === 'front' ? draw.front.includes(n) : draw.back.includes(n)
}

/**
 * 遗漏值矩阵 missMap[期号][分区key][号码] = 与上一次出现该号码的间隔期数；
 * 种子值取后端 stats.initialMiss（库内窗口前早期历史的真实遗漏，避免边界失真），
 * draws 按期号正序（旧→新）继续累加：命中重置为 0，未命中递增 1。
 */
const missMap = computed(() => {
  const map = new Map<string, Map<string, Map<number, number>>>()
  // 匹配模式只保留命中期，相邻行不是相邻期，遗漏值无从累加
  if (isMatchMode.value) return map
  const counter = new Map<string, number>()
  // 种子：窗口首期之前的历史遗漏（后端按库内全部早期历史计算）
  for (const g of groups.value) {
    for (const s of g.stats ?? []) {
      counter.set(`${g.key}#${s.number}`, s.initialMiss || 0)
    }
  }
  const draws = trendData.value?.draws ?? []
  for (const draw of draws) {
    const rowMap = new Map<string, Map<number, number>>()
    for (const g of groups.value) {
      const numMap = new Map<number, number>()
      for (const n of g.numbers) {
        const key = `${g.key}#${n}`
        const cur = counter.get(key) ?? 0
        if (isHit(draw, g, n)) {
          counter.set(key, 0)
        } else {
          const miss = cur + 1
          counter.set(key, miss)
          numMap.set(n, miss)
        }
      }
      rowMap.set(g.key, numMap)
    }
    map.set(draw.issueNumber, rowMap)
  }
  return map
})

/** 某期某分区某号码未命中时的遗漏值（距上次出现的间隔期数；匹配模式下不成立故留空） */
function missOf(draw: LotteryDraw, g: LotteryZone, n: number): number | string {
  if (isMatchMode.value) return ''
  return missMap.value.get(draw.issueNumber)?.get(g.key)?.get(n) ?? 0
}

/** 某期某分区是否断区（该分区所有号码均未命中） */
function isZoneEmpty(draw: LotteryDraw, g: LotteryZone): boolean {
  if (g.positional) return false
  return g.numbers.every(n => !isHit(draw, g, n))
}

/** 获取某期某分区的命中号码集合（仅池选型分区的横向连号判定用） */
function getHitSet(draw: LotteryDraw, g: LotteryZone): Set<number> {
  return new Set(g.numbers.filter(n => isHit(draw, g, n)))
}

/**
 * 位置型分区的竖向三连单元格集合，键为 `期号|分区key`。
 * 排列五/福彩3D 的连号看纵向：同一数位连续 3 期及以上开出同一数字，
 * 三个圈在图上同一列上下相邻。因需跳期比对，逐分区扫一遍预先算好；
 * 该类分区每期只命中 1 个号码，故键无需带号码。
 */
const verticalRunCells = computed(() => {
  const set = new Set<string>()
  // 匹配模式只保留命中期、期与期不相邻，“连续 3 期”无从判定
  if (isMatchMode.value) return set
  const draws = trendData.value?.draws ?? []
  for (const g of groups.value) {
    if (!g.positional) continue
    let runStart = 0
    for (let i = 1; i <= draws.length; i++) {
      const prev = draws[runStart].front[g.posIndex]
      // 走到末尾或数字变了，说明 [runStart, i) 是一段完整的相同数字连出
      if (i < draws.length && prev !== undefined && draws[i].front[g.posIndex] === prev) continue
      if (i - runStart >= 3) {
        for (let j = runStart; j < i; j++) set.add(`${draws[j].issueNumber}|${g.key}`)
      }
      runStart = i
    }
  }
  return set
})

/** 某号码是否属于三连号及以上的连号序列（位置型看纵向同数字连出，池选型看区内横向连号） */
function isConsecutive(draw: LotteryDraw, g: LotteryZone, n: number): boolean {
  if (!isHit(draw, g, n)) return false
  if (g.positional) return verticalRunCells.value.has(`${draw.issueNumber}|${g.key}`)
  const hitSet = getHitSet(draw, g)
  // 向两端延伸，计算包含 n 的连续段长度
  let lo = n, hi = n
  while (hitSet.has(lo - 1)) lo--
  while (hitSet.has(hi + 1)) hi++
  return (hi - lo + 1) >= 3
}

/**
 * 组选集合区（福彩3D 的组选号码分布）：彩种按位开奖但本区非位置型，
 * 即把整期各位数字当集合看，故同一数字可重复开出，展示口径与按位分区不同。
 */
function isGroupSetZone(g: LotteryZone): boolean {
  return !g.positional && groups.value.some(x => x.positional)
}

/** 该期是否重复开出某数字（如 5/5/8 中的 5），组选区据此标红 */
function isRepeatNum(draw: LotteryDraw, n: number): boolean {
  return draw.front.filter(x => x === n).length > 1
}

/**
 * 命中单元格的号码球样式。
 * 组选区：统一空心小圈，默认黑边，同一数字重复开出时标红（不叠加连号空心，避免两套口径冲突）；
 * 其余分区：前区红球/后区蓝球实心，三连号转空心。
 */
function hitClass(draw: LotteryDraw, g: LotteryZone, n: number): string[] {
  if (isGroupSetZone(g)) {
    return ['hit', 'group-hit', isRepeatNum(draw, n) ? 'repeat' : '']
  }
  return [
    g.source === 'back' ? 'hit back-hit' : 'hit front-hit',
    isConsecutive(draw, g, n) ? 'consecutive' : '',
  ]
}

// ── 右侧统计列（每个彩种展示哪几列按 SUMMARY_COLS_BY_TYPE 配置） ──
/** 前区分区（区间比按此逐区统计，位置型分区无分区语义） */
const frontGroups = computed(() => groups.value.filter(g => g.source === 'front' && !g.positional))

/** 前区候选号码全集（升序，用于大小号分界） */
const frontPool = computed(() => {
  const set = new Set<number>()
  groups.value.filter(g => g.source !== 'back').forEach(g => g.numbers.forEach(n => set.add(n)))
  return Array.from(set).sort((a, b) => a - b)
})

/** 大小号分界：号码大于该值记为大号（候选号码全集对半划分） */
const bigThreshold = computed(() => {
  const pool = frontPool.value
  if (pool.length === 0) return 0
  return pool[Math.floor(pool.length / 2) - 1] ?? pool[0]
})

/** 质数判定（0、1 按彩票惯例记为合数） */
function isPrimeNum(n: number): boolean {
  if (n < 2) return false
  for (let i = 2; i * i <= n; i++) if (n % i === 0) return false
  return true
}

/** 和值：前区号码之和 */
function sumOf(draw: LotteryDraw): number {
  return draw.front.reduce((s, n) => s + n, 0)
}

/** 和尾：和值的个位数（如和值 18 → 和尾 8） */
function sumTailOf(draw: LotteryDraw): number {
  return sumOf(draw) % 10
}

/** 跨度：前区最大号码 − 最小号码 */
function spanOf(draw: LotteryDraw): number {
  if (draw.front.length === 0) return 0
  return Math.max(...draw.front) - Math.min(...draw.front)
}

/** 区间比：前区各分区命中个数（如 2:2:1） */
function zoneRatioOf(draw: LotteryDraw): string {
  return frontGroups.value
    .map(g => g.numbers.filter(n => draw.front.includes(n)).length)
    .join(':')
}

/** 区间比是否偏态（某一分区集中 4 个及以上号码） */
function isZoneRatioExtreme(draw: LotteryDraw): boolean {
  const counts = frontGroups.value.map(g => g.numbers.filter(n => draw.front.includes(n)).length)
  return counts.some(c => c >= 4)
}

/** 通用比值：满足条件的个数 : 其余个数（如 3:2） */
function ratioOf(draw: LotteryDraw, match: (n: number) => boolean): string {
  const hit = draw.front.filter(match).length
  return `${hit}:${draw.front.length - hit}`
}

/** 比值是否偏态（两侧任一方不超过 1 个，即极度集中） */
function isRatioExtreme(draw: LotteryDraw, match: (n: number) => boolean): boolean {
  const hit = draw.front.filter(match).length
  return Math.min(hit, draw.front.length - hit) <= 1
}

/** 奇偶比：前区奇数个数 : 偶数个数（如 3:2） */
function oddEvenRatioOf(draw: LotteryDraw): string {
  return ratioOf(draw, n => n % 2 === 1)
}

/** 奇偶比是否偏态（奇数或偶数不超过 1 个） */
function isOddEvenExtreme(draw: LotteryDraw): boolean {
  return isRatioExtreme(draw, n => n % 2 === 1)
}

/** 大小比：前区大号个数 : 小号个数（如 3:2） */
function bigSmallRatioOf(draw: LotteryDraw): string {
  return ratioOf(draw, n => n > bigThreshold.value)
}

/** 大小比是否偏态（大号或小号不超过 1 个） */
function isBigSmallExtreme(draw: LotteryDraw): boolean {
  return isRatioExtreme(draw, n => n > bigThreshold.value)
}

/** 质合比：前区质数个数 : 合数个数（如 2:3） */
function primeCompRatioOf(draw: LotteryDraw): string {
  return ratioOf(draw, isPrimeNum)
}

/** 质合比是否偏态（质数或合数不超过 1 个） */
function isPrimeCompExtreme(draw: LotteryDraw): boolean {
  return isRatioExtreme(draw, isPrimeNum)
}

/** 右侧统计列定义 */
interface SummaryCol {
  key: string
  label: string
  /** 单元格取值 */
  value: (draw: LotteryDraw) => string | number
  /** 偏态判定（缺省则该列不做高亮） */
  extreme?: (draw: LotteryDraw) => boolean
  /** 偏态高亮样式类 */
  extremeClass?: string
}

/** 全部可用统计列定义（按 key 索引，供各彩种按需取用） */
const ALL_SUMMARY_COLS: Record<string, SummaryCol> = {
  sum: { key: 'sum', label: '和值', value: sumOf },
  sumTail: { key: 'sumTail', label: '和尾', value: sumTailOf },
  span: { key: 'span', label: '跨度', value: spanOf },
  zone: {
    key: 'zone', label: '区间比', value: zoneRatioOf,
    extreme: isZoneRatioExtreme, extremeClass: 'ratio-extreme-zone',
  },
  oddEven: {
    key: 'oddEven', label: '奇偶比', value: oddEvenRatioOf,
    extreme: isOddEvenExtreme, extremeClass: 'ratio-extreme-odd',
  },
  bigSmall: {
    key: 'bigSmall', label: '大小比', value: bigSmallRatioOf,
    extreme: isBigSmallExtreme, extremeClass: 'ratio-extreme-big',
  },
  primeComp: {
    key: 'primeComp', label: '质合比', value: primeCompRatioOf,
    extreme: isPrimeCompExtreme, extremeClass: 'ratio-extreme-prime',
  },
}

/** 各彩种展示的统计列及顺序（与官网走势图口径一致；增减列只需改此处） */
const SUMMARY_COLS_BY_TYPE: Record<string, string[]> = {
  DLT: ['sum', 'span', 'zone', 'oddEven'],
  SSQ: ['sum', 'span', 'zone', 'oddEven'],
  PL5: ['sum', 'oddEven', 'bigSmall', 'primeComp'],
  FC3D: ['sumTail', 'span'],
}

/** 当前彩种的右侧统计列 */
const summaryCols = computed<SummaryCol[]>(() =>
  (SUMMARY_COLS_BY_TYPE[props.type?.toUpperCase()] ?? [])
    .map(k => ALL_SUMMARY_COLS[k])
    .filter((c): c is SummaryCol => !!c))

/** 是否展示右侧统计列 */
const showSummaryCols = computed(() => groups.value.length > 0 && summaryCols.value.length > 0)

/** 右侧统计列数（操作行 colspan 用） */
const summaryColCount = computed(() => (showSummaryCols.value ? summaryCols.value.length : 0))

// ── 预选号（picks 按选号分区键存储） ──
interface Prediction {
  id: number
  name: string
  picks: Record<string, number[]>
}

const predictions = ref<Prediction[]>([
  { id: 1, name: '预选1', picks: {} },
  { id: 2, name: '预选2', picks: {} },
  { id: 3, name: '预选3', picks: {} },
])
let nextPredictionId = 4

function togglePrediction(pred: Prediction, g: LotteryZone, num: number) {
  const zone = props.pickZones.find(z => z.key === g.pickZoneKey)
  if (!zone) return
  const arr = pred.picks[g.pickZoneKey] ?? []
  const idx = arr.indexOf(num)
  if (idx >= 0) {
    arr.splice(idx, 1)
  } else if (zone.positional) {
    // 位置型每位只留 1 个数字：直接替换
    pred.picks[g.pickZoneKey] = [num]
    return
  } else {
    if (arr.length >= zone.pick) {
      ElMessage.warning({ message: `${zone.label}最多选 ${zone.pick} 个号码`, appendTo: fullscreenElement() })
      return
    }
    arr.push(num)
    arr.sort((a, b) => a - b)
  }
  pred.picks[g.pickZoneKey] = arr
}

function isPredicted(pred: Prediction, g: LotteryZone, num: number): boolean {
  return (pred.picks[g.pickZoneKey] ?? []).includes(num)
}

function addPrediction() {
  predictions.value.push({
    id: nextPredictionId++,
    name: `预选${predictions.value.length + 1}`,
    picks: {},
  })
}

function removePrediction(pred: Prediction) {
  const idx = predictions.value.findIndex(p => p.id === pred.id)
  if (idx >= 0) {
    predictions.value.splice(idx, 1)
  }
}

/** 把一条预选组装成一注：位置型按 posIndex 归位，池选型升序 */
function buildBet(pred: Prediction): { front: number[]; back: number[] } {
  const front: number[] = []
  const back: number[] = []
  const positional = props.pickZones.some(z => z.positional)
  for (const z of props.pickZones) {
    const nums = pred.picks[z.key] ?? []
    const target = z.source === 'front' ? front : back
    if (positional && z.source === 'front') {
      target[z.posIndex] = nums[0] ?? 0
    } else {
      target.push(...nums)
    }
  }
  if (!positional) {
    front.sort((a, b) => a - b)
    back.sort((a, b) => a - b)
  }
  return { front, back }
}

async function savePredictions() {
  if (props.pickZones.length === 0) return
  const valid = predictions.value.filter(
    p => props.pickZones.every(z => (p.picks[z.key] ?? []).length === z.pick),
  )
  if (valid.length === 0) {
    const rule = props.pickZones.map(z => `${z.label} ${z.pick} 个`).join(' + ')
    ElMessage.warning({ message: `请确保每条预选都选满：${rule}`, appendTo: fullscreenElement() })
    return
  }

  try {
    const bets = valid.map(buildBet)
    await httpPost('/api/Common/Lottery/Save', { type: props.type, bets })
    ElMessage.success({ message: `已保存 ${valid.length} 注到选号记录`, appendTo: fullscreenElement() })
    // 清空已保存的预选行
    predictions.value = predictions.value.filter(p => !valid.includes(p))
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '保存失败', appendTo: fullscreenElement() })
  }
}

async function loadTrend() {
  loading.value = true
  try {
    trendData.value = await getLotteryTrend(
      props.type, periods.value, dateRange.value?.[0], dateRange.value?.[1],
      activeMatch.value)
    await nextTick()
    syncStickyOffsets()
    observeTableSize()
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '加载走势图失败', appendTo: fullscreenElement() })
  } finally {
    loading.value = false
  }
}

/**
 * 同步左侧固定列的粘附偏移：列宽可能被内容撑开（如 colspan=3 的按钮行），
 * 硬编码 left 会造成列间缝隙或重叠。此处取 offsetWidth 而非 getBoundingClientRect：
 * 后者包含 sticky 位移，横向滚动后测得的值会偏移；前者是纯边框盒宽度，
 * 在 border-spacing:0 下恰好等于列进距，累加即得精确偏移。
 */
function syncStickyOffsets() {
  const wrapper = tableWrapperRef.value
  if (!wrapper) return
  const cells = wrapper.querySelectorAll<HTMLElement>('thead .zone-header-row th.sticky-col')
  if (cells.length < 3) return
  const [w1, w2, w3] = Array.from(cells, c => c.offsetWidth)
  wrapper.style.setProperty('--sticky-l2', `${w1}px`)
  wrapper.style.setProperty('--sticky-l3', `${w1 + w2}px`)
  wrapper.style.setProperty('--sticky-total', `${w1 + w2 + w3}px`)
}

// ── 号码连线（逐期连接命中号码的折线） ──
/**
 * 需要画连线的分区：只有“每期恰好命中 1 个号码”的分区才有折线语义
 * （排列五/福彩3D 的各数位、双色球蓝球）。按实际数据判定而不看选号配置：
 * 大乐透后区每期 2 个、红球各区每期 0-6 个、福彩3D 组选分布区每期 2-3 个，均自动排除。
 */
const lineZoneKeys = computed(() => {
  const keys = new Set<string>()
  // 匹配模式只保留命中期，相邻行不是相邻期，逐期折线失去意义
  if (isMatchMode.value) return keys
  const draws = trendData.value?.draws ?? []
  if (draws.length === 0) return keys
  for (const g of groups.value) {
    if (draws.every(d => g.numbers.filter(n => isHit(d, g, n)).length === 1)) keys.add(g.key)
  }
  return keys
})

/** 一个分区的连线 */
interface TrendLine {
  key: string
  /** SVG polyline 折点（相对表格左上角） */
  points: string
  /** 后区（蓝球）用蓝色，前区用红色，与号码球配色一致 */
  back: boolean
}

const trendLines = ref<TrendLine[]>([])
/** 连线 SVG 画布尺寸（跟随表格） */
const lineCanvas = ref({ w: 0, h: 0 })

/**
 * 测算连线折点：逐分区取各期命中单元格的中心点。
 * td 的 offsetLeft/offsetTop 相对 <table>（表格就是单元格的 offsetParent），
 * 与覆盖在其上、同样从表格原点起算的 SVG 坐标系一致，
 * 因此列宽变化与横向滚动都不会让连线错位。
 */
function computeTrendLines() {
  const table = tableWrapperRef.value?.querySelector('table')
  if (!table) {
    trendLines.value = []
    return
  }
  const lines: TrendLine[] = []
  for (const g of groups.value) {
    if (!lineZoneKeys.value.has(g.key)) continue
    const cells = table.querySelectorAll<HTMLElement>(`tbody td[data-line="${g.key}"]`)
    // 单点连不成线
    if (cells.length < 2) continue
    const points = Array.from(cells,
      c => `${c.offsetLeft + c.offsetWidth / 2},${c.offsetTop + c.offsetHeight / 2}`).join(' ')
    lines.push({ key: g.key, points, back: g.source === 'back' })
  }
  trendLines.value = lines
  lineCanvas.value = { w: table.offsetWidth, h: table.offsetHeight }
}

/**
 * 表格尺寸一变（窗口缩放、全屏切换、预选行增减）折点就失效，靠观察器重算。
 * 这里只重算连线、不顺带调 syncStickyOffsets：后者会回写列宽变量改变表格布局，
 * 与观察器互相激发可能振荡；而 SVG 是绝对定位、脱离文档流的，改它不会反咬表格。
 */
let lineObserver: ResizeObserver | null = null
function observeTableSize() {
  const table = tableWrapperRef.value?.querySelector('table')
  if (!table) return
  lineObserver ??= new ResizeObserver(() => computeTrendLines())
  lineObserver.disconnect()
  // observe 会立即回调一次，初次测算由此完成
  lineObserver.observe(table)
}
onUnmounted(() => {
  lineObserver?.disconnect()
  lineObserver = null
})

// ── 官网通告弹窗（双击某期行触发，共享组件） ──
const noticeDialogRef = ref<InstanceType<typeof LotteryNoticeDialog>>()

// ── 玩法规则弹窗（工具栏按钮触发，共享组件） ──
const ruleDialogRef = ref<InstanceType<typeof LotteryRuleDialog>>()

onMounted(() => loadTrend())

// 菜单切彩种时父组件复用不重新挂载：type 变化时重置查询条件并重拉走势图
watch(() => props.type, () => {
  dateRange.value = null
  trendData.value = null
  // 各彩种号码池不同，旧彩种的匹配号码在新彩种下可能越界，一律重置
  activeMatch.value = null
  // 旧彩种的折点对新表格无意义，先清掉避免重测前闪一帧
  trendLines.value = []
  loadTrend()
})

// ── 全屏展示（浏览器 Fullscreen API，作用于走势图容器） ──
const containerRef = ref<HTMLElement>()
const isFullscreen = ref(false)

async function toggleFullscreen() {
  try {
    if (document.fullscreenElement) {
      await document.exitFullscreen()
    } else if (containerRef.value) {
      await containerRef.value.requestFullscreen()
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

// 窗口尺寸变化会改变单元格渲染宽度，需重新测算固定列偏移
onMounted(() => window.addEventListener('resize', syncStickyOffsets))
onUnmounted(() => window.removeEventListener('resize', syncStickyOffsets))

// ── 工具函数 ──
const weekDays = ['日', '一', '二', '三', '四', '五', '六']
function getWeekDay(dateStr?: string): string {
  if (!dateStr) return ''
  const d = new Date(dateStr)
  if (isNaN(d.getTime())) return ''
  return weekDays[d.getDay()]
}
</script>

<style scoped>
.trend-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #fff;
}

/* 全屏时保持白底并铺满屏幕 */
.trend-container:fullscreen {
  background: #fff;
  width: 100%;
  height: 100%;
}

.trend-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid #f0f0f0;
  flex-shrink: 0;
  /* 工具栏恒为单行：窗口变窄时靠左侧输入类控件收窄消化，不允许折行 */
  flex-wrap: nowrap;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: nowrap;
  /* flex 项默认 min-width:auto 不会小于内容，须放开才能收窄 */
  min-width: 0;
}

/* 期数按钮组自身默认 flex-wrap: wrap，被挤压时会折行，须显式禁掉 */
.toolbar-left :deep(.el-radio-group) {
  flex-wrap: nowrap;
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
  /* 右侧操作按钮不参与收窄，始终完整显示 */
  flex-shrink: 0;
}

/* 筛选条件分组分隔竖线（期数/日期 与 历史匹配 是两组互斥条件） */
.toolbar-sep {
  width: 1px;
  height: 18px;
  background: #e8e8e8;
}

/* 匹配结果提示（匹配号码与命中期数，比普通提示显著） */
.toolbar-match-tip {
  font-size: 12px;
  color: #e6a23c;
  white-space: nowrap;
}

.toolbar-label {
  font-size: 13px;
  color: #606266;
  /* 被挤压时标签文字不折行 */
  white-space: nowrap;
}

/* 日期区间控件默认 350px，是工具栏最大头，压窄以保障单行 */
.toolbar-daterange {
  width: 260px;
  min-width: 0;
}

.toolbar-hint {
  font-size: 12px;
  color: #c0c4cc;
  white-space: nowrap;
}

.trend-content {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.trend-scroll {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.trend-section {
  margin-bottom: 0;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

/* 分区样式 */
.zone-header-row th {
  font-size: 12px;
  font-weight: 600;
  padding: 3px 4px;
  border-bottom: 2px solid #e8e8e8;
}

.zone-header {
  border-left: 2px solid #d9d9d9 !important;
}

.zone-start {
  border-left: 2px solid #d9d9d9 !important;
}

.zone-1 { background: rgba(245, 230, 230, 0.25); }
.zone-2 { background: rgba(230, 240, 250, 0.25); }
.zone-3 { background: rgba(230, 250, 235, 0.25); }
.zone-back { background: rgba(240, 230, 250, 0.25); }

thead .zone-1 { background: rgba(245, 230, 230, 0.45); }
thead .zone-2 { background: rgba(230, 240, 250, 0.45); }
thead .zone-3 { background: rgba(230, 250, 235, 0.45); }
thead .zone-back { background: rgba(240, 230, 250, 0.45); }

tfoot .zone-1 { background: rgba(245, 230, 230, 0.3); }
tfoot .zone-2 { background: rgba(230, 240, 250, 0.3); }
tfoot .zone-3 { background: rgba(230, 250, 235, 0.3); }
tfoot .zone-back { background: rgba(240, 230, 250, 0.3); }

.trend-table-wrapper {
  /* 左侧三个固定列的列宽（定义列宽用） */
  --sticky-w1: 84px;
  --sticky-w2: 88px;
  --sticky-w3: 60px;
  /* 第 2/3 列的粘附左偏移与三列合计宽：挂载后由 syncStickyOffsets 按实际列宽校正 */
  --sticky-l2: 84px;
  --sticky-l3: 172px;
  --sticky-total: 232px;
  flex: 1;
  overflow: auto;
  /* 号码连线 SVG 绝对定位于此，随内容一起滚动 */
  position: relative;
  border: 1px solid #e8e8e8;
  border-radius: 8px;
  min-height: 0;
}

/*
 * 必须用 separate + border-spacing:0 而不能用 collapse：
 * 折叠边框模式下边框由表格绘制、不属于单元格，sticky 列位移时边框留在原地，
 * 列缝会露出缝隙让下层号码球透出；且单元格 offsetWidth 与实际列进距不等，固定列 left 无法算准。
 */
.trend-table {
  border-collapse: separate;
  border-spacing: 0;
  width: 100%;
  font-size: 12px;
}

.trend-table th,
.trend-table td {
  border: none;
  border-bottom: 1px solid #f0f0f0;
  border-right: 1px solid #f5f5f5;
  padding: 5px 2px;
  text-align: center;
  white-space: nowrap;
}

.trend-table thead th {
  background: #fafafa;
  font-weight: 600;
  color: #303133;
  position: sticky;
  z-index: 2;
  border-bottom: 2px solid #e8e8e8;
}

.trend-table thead .zone-header-row th {
  top: 0;
  padding: 0;
  height: 20px;
  font-size: 11px;
  line-height: 20px;
  color: #909399;
  z-index: 3;
  border: none;
}

/* 跨两行的表头单元格（期号、日期、星期）：
 * separate 模式下边框不再与邻居共享，需自行补回右边框与表头分隔线 */
.trend-table thead .zone-header-row th[rowspan="2"] {
  height: auto;
  font-size: 12px;
  line-height: 1.4;
  color: #303133;
  z-index: 25;
  background: #fafafa;
  vertical-align: middle;
  border-right: 1px solid #f5f5f5;
  border-bottom: 2px solid #e8e8e8;
}

.trend-table thead tr:not(.zone-header-row) th {
  top: 20px;
}

/* 固定列 */
.sticky-col {
  position: sticky;
  z-index: 10;
  background: #fff;
}

thead .sticky-col { z-index: 20; background: #fafafa; }
tfoot .sticky-col { z-index: 16; background: #fcfcfc; }

.issue-cell {
  font-weight: 600;
  color: #303133;
  box-sizing: border-box;
  width: var(--sticky-w1);
  min-width: var(--sticky-w1);
  left: 0;
  background: #fff;
}
thead .issue-cell { background: #fafafa; }
tfoot .issue-cell { background: #fcfcfc; }

.date-cell {
  color: #606266;
  box-sizing: border-box;
  width: var(--sticky-w2);
  min-width: var(--sticky-w2);
  left: var(--sticky-l2);
  background: #fff;
}
thead .date-cell { background: #fafafa; }
tfoot .date-cell { background: #fcfcfc; }

.week-cell {
  color: #e6a23c;
  box-sizing: border-box;
  width: var(--sticky-w3);
  min-width: var(--sticky-w3);
  font-size: 12px;
  font-weight: 500;
  left: var(--sticky-l3);
  background: #fff;
}
thead .week-cell { background: #fafafa; }
tfoot .week-cell { background: #fcfcfc; }

/* 固定列右侧阴影 */
.week-cell::after {
  content: '';
  position: absolute;
  top: 0;
  right: -8px;
  bottom: 0;
  width: 8px;
  background: linear-gradient(to right, rgba(0,0,0,0.04), transparent);
  pointer-events: none;
}

/* 号码格子 */
.num-header {
  min-width: 30px;
  font-size: 11px;
  color: #909399;
  font-weight: 500;
}

.num-cell {
  min-width: 30px;
  height: 26px;
  position: relative;
  padding: 2px;
}

.num-cell.hit {
  font-weight: 600;
}

.num-cell.hit span {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  color: #fff;
  font-size: 11px;
  font-weight: 600;
  /* 号码球须盖在连线（z-index 0）之上，否则折线会横穿球面；
   * 但必须低于表头（.trend-table thead th 为 2），否则竖向滚动时球会糊在固定表头上 */
  position: relative;
  z-index: 1;
}

/* ── 号码连线 ── */
/* 绝对定位覆盖整张表格并随内容滚动。
 * z-index 取 0：与单元格（position:relative、z-index:auto）同层，靠 DOM 顺序排在表格之后
 * 而盖住单元格底色；同时低于号码球(1)、表头(2/3)、固定列(10)与表尾(12/16)，
 * 故滚到冻结区域后自然被遮住。切记不能高于 2，否则折线会糊在固定表头上。 */
.trend-lines {
  position: absolute;
  top: 0;
  left: 0;
  z-index: 0;
  pointer-events: none;
}

.trend-lines polyline {
  fill: none;
  stroke-width: 1.5;
  stroke-linejoin: round;
  opacity: 0.7;
}

.trend-lines .line-front { stroke: #e6393a; }
.trend-lines .line-back { stroke: #409eff; }

.front-hit span {
  background: #e6393a;
}

.back-hit span {
  background: #409eff;
}

/* 连号空心展示 */
.num-cell.consecutive.hit span {
  background: transparent !important;
  border: 2px solid #e6393a;
  color: #e6393a;
}

.num-cell.consecutive.back-hit span {
  border-color: #409eff;
  color: #409eff;
}

/* 组选号码分布区：比按位分区的球小一圈的空心圆，默认黑边；
 * 写在 .num-cell.hit span 之后，同特异度下靠顺序覆盖其实心白字样式 */
.num-cell.group-hit span {
  width: 18px;
  height: 18px;
  background: transparent;
  border: 1px solid #303133;
  color: #303133;
}

/* 同一数字在本期重复开出（如 5/5/8）时标红 */
.num-cell.group-hit.repeat span {
  border-color: #e6393a;
  color: #e6393a;
}

/* 断区高亮：该分区所有号码均未开出时整区着色 */
.num-cell.zone-empty {
  background: rgba(250, 218, 94, 0.18) !important;
}

.num-cell.miss.zone-empty {
  color: #c8b560;
}

.num-cell.miss {
  color: #dcdfe6;
}

/* ── 右侧统计列（和值/跨度/区间比/奇偶比） ── */
.summary-header {
  min-width: 56px;
  font-size: 12px;
  font-weight: 600;
  color: #8c6d3f;
  background: rgba(250, 240, 220, 0.85) !important;
  vertical-align: middle;
}

.summary-cell {
  min-width: 56px;
  font-size: 12px;
  font-weight: 500;
  color: #606266;
  background: rgba(250, 245, 235, 0.5);
  padding: 3px 6px;
}

/* 统计列区域起始分隔线 */
.summary-start {
  border-left: 2px solid #d9d9d9 !important;
}

/* 区间比偏态（某分区集中 4 个及以上）橙色标记 */
.ratio-extreme-zone {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 3px;
  background: #f0b45f;
  color: #fff;
  font-weight: 600;
}

/* 奇偶比偏态（奇数或偶数≤ 1 个）青色标记 */
.ratio-extreme-odd {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 3px;
  background: #5fa8a0;
  color: #fff;
  font-weight: 600;
}

/* 大小比偏态（大号或小号≤ 1 个）蓝紫色标记 */
.ratio-extreme-big {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 3px;
  background: #7b95c9;
  color: #fff;
  font-weight: 600;
}

/* 质合比偏态（质数或合数≤ 1 个）紫色标记 */
.ratio-extreme-prime {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 3px;
  background: #b07ba8;
  color: #fff;
  font-weight: 600;
}

/* 预选行 */
.prediction-row td {
  background: #fffbe6;
  padding: 3px 2px;
  cursor: pointer;
  border-bottom: 1px solid #f5e6c0;
}

.prediction-label {
  text-align: left !important;
  padding-left: 8px !important;
  font-size: 12px;
  font-weight: 600;
  color: #e6a23c;
  position: sticky !important;
  left: 0 !important;
  min-width: var(--sticky-total);
  z-index: 17 !important;
  background: #fffbe6 !important;
}

.prediction-remove {
  display: inline-block;
  margin-left: 6px;
  width: 16px;
  height: 16px;
  line-height: 14px;
  text-align: center;
  border-radius: 50%;
  background: #f56c6c;
  color: #fff;
  font-size: 12px;
  cursor: pointer;
  opacity: 0.6;
}

.prediction-remove:hover {
  opacity: 1;
}

.prediction-cell {
  min-width: 30px;
  height: 24px;
  position: relative;
  padding: 2px;
  transition: all 0.15s;
  color: #909399;
  font-size: 11px;
  text-align: center;
}

.prediction-cell:hover {
  background: rgba(230, 162, 60, 0.15) !important;
  color: #606266;
}

.prediction-cell.selected {
  background: transparent !important;
}

.prediction-cell.selected span {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: #e6393a;
  color: #fff;
  font-size: 11px;
  font-weight: 600;
}

/* 后区/蓝球预选选中显示蓝色 */
.prediction-cell.zone-back.selected span {
  background: #2563eb;
}

.prediction-action-row td {
  background: #fffbe6;
  padding: 6px 8px;
  border-bottom: 2px solid #f5e6c0;
}

.prediction-action-label {
  position: sticky !important;
  left: 0 !important;
  min-width: var(--sticky-total);
  z-index: 17 !important;
  background: #fffbe6 !important;
}

/* 统计行 */
.stat-row td {
  background: #fcfcfc;
  font-weight: 500;
  font-size: 11px;
  border-bottom: none;
}

.stat-label {
  text-align: left !important;
  padding-left: 12px !important;
  color: #909399;
  font-size: 12px;
  font-weight: 500;
  position: sticky !important;
  left: 0 !important;
  min-width: var(--sticky-total);
  z-index: 17 !important;
  background: #fcfcfc !important;
}

.stat-cell { color: #606266; }
.miss-cell { color: #e6a23c; font-weight: 600; }

/* 统计行：粘底固定，行高须与 STAT_ROW_HEIGHT 一致，bottom 由行内样式逐行叠加 */
tfoot .stat-row td {
  position: sticky;
  box-sizing: border-box;
  height: 24px;
  line-height: 14px;
  padding: 4px 2px;
  z-index: 12;
  background: #fcfcfc;
  border-top: 1px solid #e8e8e8;
}

/* 斑马纹 */
.trend-table tbody tr:nth-child(even) td {
  background-color: #fafbfc;
}
.trend-table tbody tr:nth-child(even) .issue-cell,
.trend-table tbody tr:nth-child(even) .date-cell,
.trend-table tbody tr:nth-child(even) .week-cell {
  background-color: #fafbfc;
}
.trend-table tbody tr:nth-child(even) .summary-cell {
  background-color: rgba(248, 243, 233, 0.6);
}

/* 悬停高亮 */
.trend-table tbody tr:hover td {
  background-color: #f0f7ff !important;
}
.trend-table tbody tr:hover .issue-cell,
.trend-table tbody tr:hover .date-cell,
.trend-table tbody tr:hover .week-cell {
  background-color: #f0f7ff !important;
}

/* 选中行 */
.trend-table tbody tr.selected-row td {
  background-color: #e6f0ff !important;
}
.trend-table tbody tr.selected-row .issue-cell,
.trend-table tbody tr.selected-row .date-cell,
.trend-table tbody tr.selected-row .week-cell {
  background-color: #e6f0ff !important;
}
.selected-row {
  cursor: pointer;
}
.trend-table tbody tr {
  cursor: pointer;
}
.front-ball { background: #e6393a; }
.back-ball { background: #2563eb; }
</style>
