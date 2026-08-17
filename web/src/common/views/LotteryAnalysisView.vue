<script setup lang="ts">
/**
 * 彩票智能分析页面：基于历史数据的多维度评分与号码推荐
 * - 评分热力图：号码网格按综合评分着色
 * - 推荐号码：精选前区 + 后区
 * - AI 组合：自动生成 3~5 注完整号码
 * - 热号/冷号双列对比
 * - 分析摘要
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { httpPost } from '@/api/request'
import {
  getLotteryAnalysis, getLotteryConfig, fmtNumber,
  type LotteryType, type LotteryConfig, type LotteryAnalysis,
} from '@/common/lottery'

/** 公开模式：外部链接访问时隐藏保存按钮 */
const props = defineProps<{ publicMode?: boolean }>()
const route = useRoute()
const publicMode = computed(() => props.publicMode || route.name === 'lottery-analysis-public')

const LOTTERY_OPTIONS = [
  { value: 'DLT', label: '大乐透' },
  { value: 'SSQ', label: '双色球' },
  { value: 'PL5', label: '排列五' },
  { value: 'FC3D', label: '福彩3D' },
]

const type = ref<LotteryType>('DLT')
const periods = ref(100)
const loading = ref(false)
const analysis = ref<LotteryAnalysis | null>(null)
const config = ref<LotteryConfig | null>(null)
const positional = computed(() => type.value === 'PL5' || type.value === 'FC3D')

async function loadConfig() {
  try { config.value = await getLotteryConfig(type.value) } catch { config.value = null }
}

async function runAnalysis() {
  loading.value = true
  try {
    analysis.value = await getLotteryAnalysis(type.value, periods.value)
  } catch (e: any) {
    ElMessage.error(e?.message || '分析失败')
    analysis.value = null
  } finally {
    loading.value = false
  }
}

onMounted(async () => { await loadConfig(); await runAnalysis() })

function onTypeChange() { loadConfig().then(() => runAnalysis()) }

/** 号码格式化（池选型补零，位置型直接显示） */
function fmt(n: number): string { return fmtNumber(positional.value, n) }

/** 评分 → 背景色（0~100 映射 灰→黄→红） */
function scoreColor(score: number): string {
  const ratio = Math.min(score / 100, 1)
  if (ratio < 0.4) {
    const g = Math.round(200 + ratio / 0.4 * 55)
    return `rgb(${g}, ${g}, ${g})`
  }
  if (ratio < 0.7) {
    const t = (ratio - 0.4) / 0.3
    const r = Math.round(255)
    const g = Math.round(255 - t * 60)
    return `rgb(${r}, ${g}, 80)`
  }
  const t = (ratio - 0.7) / 0.3
  return `rgb(${Math.round(255 - t * 30)}, ${Math.round(195 - t * 115)}, ${Math.round(80 - t * 50)})`
}

/** 评分 → 文字色 */
function textColor(score: number): string {
  return score > 55 ? '#fff' : '#303133'
}

/** 推荐号码是否在集合中 */
function isRecommendedFront(n: number): boolean {
  return analysis.value?.recommendedFront.includes(n) ?? false
}
function isRecommendedBack(n: number): boolean {
  return analysis.value?.recommendedBack.includes(n) ?? false
}

/** 一键保存 AI 组合到选号记录 */
async function saveBets() {
  if (!analysis.value || analysis.value.generatedBets.length === 0) return
  try {
    await httpPost('/api/Common/Lottery/Save', {
      type: type.value,
      bets: analysis.value.generatedBets,
    })
    ElMessage.success(`已保存 ${analysis.value.generatedBets.length} 注 AI 组合`)
  } catch (e: any) {
    ElMessage.error(e?.message || '保存失败')
  }
}

/** 评分明细合并行（前区 + 后区） */
const allScoreRows = computed(() => {
  if (!analysis.value) return []
  const rows = analysis.value.frontScores.map(s => ({
    zone: s.zoneLabel || (positional.value ? '' : (config.value?.pickZones[0]?.label ?? '前区')),
    source: 'front',
    number: s.number,
    numberText: fmt(s.number),
    score: s.score,
    hotScore: s.hotScore,
    coldScore: s.coldScore,
    missScore: s.missScore,
    consecutiveScore: s.consecutiveScore,
    zoneScore: s.zoneScore,
    currentMiss: s.currentMiss,
    avgMiss: s.avgMiss,
    count: s.count,
  }))
  if (analysis.value.backScores.length > 0) {
    const backLabel = config.value?.pickZones.find(z => z.source === 'back')?.label ?? '后区'
    rows.push(...analysis.value.backScores.map(s => ({
      zone: s.zoneLabel || backLabel,
      source: 'back',
      number: s.number,
      numberText: fmt(s.number),
      score: s.score,
      hotScore: s.hotScore,
      coldScore: s.coldScore,
      missScore: s.missScore,
      consecutiveScore: s.consecutiveScore,
      zoneScore: s.zoneScore,
      currentMiss: s.currentMiss,
      avgMiss: s.avgMiss,
      count: s.count,
    })))
  }
  return rows
})

/** 位置型彩种：按 zoneLabel 分组的热力图分区 */
const heatmapZones = computed(() => {
  if (!analysis.value) return []
  if (!positional.value) return []
  const map = new Map<string, typeof analysis.value.frontScores>()
  for (const s of analysis.value.frontScores) {
    const label = s.zoneLabel || '?'
    if (!map.has(label)) map.set(label, [])
    map.get(label)!.push(s)
  }
  return Array.from(map.entries()).map(([label, scores]) => ({ label, scores }))
})

/** 位置型推荐号码：每位一个号码，与 pickZones 对应 */
const positionalRecommend = computed(() => {
  if (!analysis.value || !positional.value || !config.value) return []
  const zones = config.value.pickZones
  const nums = analysis.value.recommendedFront
  return zones.map((z, i) => ({ label: z.label, number: nums[i] ?? 0 }))
})
</script>

<template>
  <div class="analysis-page">
    <!-- 顶部控制栏 -->
    <div class="analysis-toolbar">
      <div class="toolbar-left">
        <el-radio-group v-model="type" size="default" @change="onTypeChange">
          <el-radio-button v-for="opt in LOTTERY_OPTIONS" :key="opt.value" :value="opt.value">
            {{ opt.label }}
          </el-radio-button>
        </el-radio-group>
        <span class="toolbar-label">分析期数：</span>
        <el-radio-group v-model="periods" size="default" @change="runAnalysis">
          <el-radio-button :value="50">50 期</el-radio-button>
          <el-radio-button :value="100">100 期</el-radio-button>
          <el-radio-button :value="200">200 期</el-radio-button>
          <el-radio-button :value="500">500 期</el-radio-button>
        </el-radio-group>
        <el-button type="primary" :loading="loading" @click="runAnalysis" style="margin-left: 8px;">
          {{ loading ? '分析中…' : '重新分析' }}
        </el-button>
      </div>
      <div class="toolbar-right" v-if="analysis">
        <span class="next-issue" v-if="analysis.nextIssue">
          下一期：{{ analysis.nextIssue }}
          <template v-if="analysis.nextDrawDate">（{{ analysis.nextDrawDate }}）</template>
        </span>
      </div>
    </div>

    <div v-loading="loading" class="analysis-body">
      <template v-if="analysis">
        <!-- 推荐号码卡片 -->
        <div class="card recommend-card">
          <div class="card-title">精选推荐<span class="card-subtitle">基于 {{ analysis.periods }} 期数据综合评分</span></div>
          <!-- 池选型 -->
          <template v-if="!positional">
            <div class="recommend-row">
              <span class="zone-label">{{ config?.pickZones[0]?.label ?? '前区' }}</span>
              <div class="ball-row">
                <span v-for="n in analysis.recommendedFront" :key="'rf' + n"
                  class="ball ball-front recommend-ball">{{ fmt(n) }}</span>
              </div>
            </div>
            <div class="recommend-row" v-if="analysis.recommendedBack.length > 0">
              <span class="zone-label">{{ config?.pickZones[1]?.label ?? '后区' }}</span>
              <div class="ball-row">
                <span v-for="n in analysis.recommendedBack" :key="'rb' + n"
                  class="ball ball-back recommend-ball">{{ fmt(n) }}</span>
              </div>
            </div>
          </template>
          <!-- 位置型：每位单独展示 -->
          <template v-else>
            <div class="recommend-row">
              <span v-for="r in positionalRecommend" :key="r.label" class="positional-recommend-item">
                <span class="zone-label">{{ r.label }}</span>
                <span class="ball ball-front recommend-ball">{{ fmt(r.number) }}</span>
              </span>
            </div>
          </template>
        </div>

        <!-- 热号 / 冷号对比 -->
        <div class="card-row">
          <div class="card hot-card">
            <div class="card-title">热号 TOP 10</div>
            <div class="ball-row">
              <span v-for="n in analysis.hotNumbers" :key="'h' + n" class="ball ball-hot">{{ fmt(n) }}</span>
            </div>
          </div>
          <div class="card cold-card">
            <div class="card-title">冷号 TOP 10</div>
            <div class="ball-row">
              <span v-for="n in analysis.coldNumbers" :key="'c' + n" class="ball ball-cold">{{ fmt(n) }}</span>
            </div>
          </div>
        </div>

        <!-- 评分热力图 -->
        <div class="card heatmap-card">
          <div class="card-title">评分热力图<span class="card-subtitle">颜色越深评分越高</span></div>
          <!-- 池选型：前区 -->
          <template v-if="!positional">
            <div class="heatmap-section">
              <div class="heatmap-label">{{ config?.pickZones[0]?.label ?? '前区' }}</div>
              <div class="heatmap-grid">
                <div v-for="s in analysis.frontScores" :key="'fs' + s.number" class="heatmap-cell"
                  :style="{ background: scoreColor(s.score), color: textColor(s.score) }"
                  :title="`${fmt(s.number)}: 综合 ${s.score} | 热 ${s.hotScore} | 冷 ${s.coldScore} | 遗漏 ${s.currentMiss}期`">
                  <div class="cell-number">{{ fmt(s.number) }}</div>
                  <div class="cell-score">{{ s.score.toFixed(0) }}</div>
                  <div v-if="isRecommendedFront(s.number)" class="cell-star">★</div>
                </div>
              </div>
            </div>
            <!-- 后区 -->
            <div class="heatmap-section" v-if="analysis.backScores.length > 0">
              <div class="heatmap-label">{{ config?.pickZones[1]?.label ?? '后区' }}</div>
              <div class="heatmap-grid heatmap-grid-back">
                <div v-for="s in analysis.backScores" :key="'bs' + s.number" class="heatmap-cell"
                  :style="{ background: scoreColor(s.score), color: textColor(s.score) }"
                  :title="`${fmt(s.number)}: 综合 ${s.score} | 热 ${s.hotScore} | 冷 ${s.coldScore} | 遗漏 ${s.currentMiss}期`">
                  <div class="cell-number">{{ fmt(s.number) }}</div>
                  <div class="cell-score">{{ s.score.toFixed(0) }}</div>
                  <div v-if="isRecommendedBack(s.number)" class="cell-star">★</div>
                </div>
              </div>
            </div>
          </template>
          <!-- 位置型：每位独立分区 -->
          <template v-else>
            <div v-for="zone in heatmapZones" :key="zone.label" class="heatmap-section">
              <div class="heatmap-label">{{ zone.label }}</div>
              <div class="heatmap-grid heatmap-grid-positional">
                <div v-for="s in zone.scores" :key="zone.label + s.number" class="heatmap-cell"
                  :style="{ background: scoreColor(s.score), color: textColor(s.score) }"
                  :title="`${s.number}: 综合 ${s.score} | 热 ${s.hotScore} | 冷 ${s.coldScore} | 遗漏 ${s.currentMiss}期`">
                  <div class="cell-number">{{ s.number }}</div>
                  <div class="cell-score">{{ s.score.toFixed(0) }}</div>
                </div>
              </div>
            </div>
          </template>
        </div>

        <!-- 评分详情表格 -->
        <div class="card">
          <div class="card-title">评分明细</div>
          <el-table :data="allScoreRows" size="small" stripe max-height="400"
            :default-sort="{ prop: 'score', order: 'descending' }">
            <el-table-column prop="zone" label="分区" width="100" />
            <el-table-column prop="number" label="号码" width="100">
              <template #default="{ row }">
                <span :class="['score-ball', row.source === 'back' ? 'back' : 'front']">{{ row.numberText }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="score" label="综合" width="100" sortable />
            <el-table-column prop="hotScore" label="热号" width="100" sortable />
            <el-table-column prop="coldScore" label="冷号回补" width="100" sortable />
            <el-table-column prop="missScore" label="遗漏极值" width="100" sortable />
            <el-table-column prop="consecutiveScore" label="连号" width="100" sortable />
            <el-table-column prop="zoneScore" label="区间" width="100" sortable />
            <el-table-column prop="currentMiss" label="当前遗漏" width="100" sortable />
            <el-table-column prop="avgMiss" label="平均遗漏" width="100" />
            <el-table-column prop="count" label="出现次数" width="100" sortable />
          </el-table>
        </div>

        <!-- AI 组合 -->
        <div class="card" v-if="analysis.generatedBets.length > 0">
          <div class="card-title">
            AI 组合
            <el-button v-if="!publicMode" size="small" type="primary" plain @click="saveBets" style="margin-left: auto;">
              一键保存全部
            </el-button>
          </div>
          <div class="bets-list">
            <div v-for="(bet, bi) in analysis.generatedBets" :key="bi" class="bet-row">
              <span class="bet-index">第 {{ bi + 1 }} 注</span>
              <span v-for="n in bet.front" :key="'bf' + bi + n" class="ball ball-front ball-sm">{{ fmt(n) }}</span>
              <template v-if="bet.back.length > 0">
                <span class="plus">+</span>
                <span v-for="n in bet.back" :key="'bb' + bi + n" class="ball ball-back ball-sm">{{ fmt(n) }}</span>
              </template>
            </div>
          </div>
        </div>

        <!-- 分析摘要 -->
        <div class="card summary-card" v-if="analysis.summary">
          <div class="card-title">分析摘要</div>
          <pre class="summary-text">{{ analysis.summary }}</pre>
        </div>
      </template>

      <el-empty v-else-if="!loading" description="暂无分析数据，请选择彩种后点击「重新分析」" />
    </div>
  </div>
</template>

<style scoped>
.analysis-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #f5f7fa;
  overflow: hidden;
}

.analysis-toolbar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  flex-wrap: wrap;
  gap: 8px;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.toolbar-label {
  font-size: 13px;
  color: #606266;
  margin-left: 4px;
}

.next-issue {
  font-size: 13px;
  color: #909399;
}

.analysis-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* ── 卡片通用 ── */
.card {
  background: #fff;
  border-radius: 10px;
  padding: 16px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}

.card-title {
  font-size: 15px;
  font-weight: 600;
  color: #303133;
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}


.card-subtitle {
  font-size: 12px;
  font-weight: 400;
  color: #909399;
  margin-left: 8px;
}

.card-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

/* ── 推荐号码 ── */
.recommend-card {
  background: linear-gradient(135deg, #667eea11, #764ba211);
  border: 1px solid #667eea33;
}

.recommend-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}

.zone-label {
  font-size: 13px;
  color: #606266;
  min-width: 40px;
}

.ball-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ball {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  font-weight: 600;
  font-size: 14px;
  width: 36px;
  height: 36px;
  user-select: none;
}

.ball-sm { width: 30px; height: 30px; font-size: 12px; }

.ball-front {
  background: linear-gradient(135deg, #e74c3c, #c0392b);
  color: #fff;
}

.ball-back {
  background: linear-gradient(135deg, #3498db, #2980b9);
  color: #fff;
}

.ball-hot {
  background: linear-gradient(135deg, #ff6b35, #e74c3c);
  color: #fff;
}

.ball-cold {
  background: linear-gradient(135deg, #74b9ff, #0984e3);
  color: #fff;
}

.recommend-ball {
  width: 42px;
  height: 42px;
  font-size: 16px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

/* ── 热力图 ── */
.heatmap-section {
  margin-bottom: 12px;
}

.heatmap-label {
  font-size: 13px;
  font-weight: 500;
  color: #606266;
  margin-bottom: 8px;
}

.heatmap-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
  gap: 4px;
}

.heatmap-grid-back {
  grid-template-columns: repeat(auto-fill, minmax(56px, 1fr));
}

.heatmap-cell {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  padding: 6px 2px;
  cursor: default;
  transition: transform 0.15s;
  min-height: 48px;
}

.heatmap-cell:hover {
  transform: scale(1.08);
  z-index: 1;
}

.cell-number {
  font-size: 14px;
  font-weight: 700;
  line-height: 1.2;
}

.cell-score {
  font-size: 10px;
  opacity: 0.85;
  line-height: 1.2;
}

.cell-star {
  position: absolute;
  top: -2px;
  right: -2px;
  font-size: 10px;
  color: #ffd700;
  text-shadow: 0 0 3px rgba(0, 0, 0, 0.3);
}

/* ── 评分明细 ── */
.score-ball {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
}

.score-ball.front { background: #e74c3c; }
.score-ball.back { background: #3498db; }

/* ── AI 组合 ── */
.bets-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.bet-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  background: #f8f9fa;
  border-radius: 8px;
}

.bet-index {
  font-size: 12px;
  color: #909399;
  min-width: 52px;
}

.plus {
  font-size: 14px;
  color: #c0c4cc;
  margin: 0 2px;
}

/* ── 摘要 ── */
.summary-text {
  font-family: inherit;
  font-size: 13px;
  color: #606266;
  line-height: 1.8;
  white-space: pre-wrap;
  margin: 0;
  background: #fafafa;
  padding: 12px;
  border-radius: 6px;
  border: 1px solid #ebeef5;
}

/* ── 位置型推荐 ── */
.positional-recommend-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-right: 16px;
}

.positional-recommend-item .zone-label {
  font-size: 13px;
  color: #606266;
  min-width: 28px;
}

/* ── 位置型热力图 ── */
.heatmap-grid-positional {
  grid-template-columns: repeat(10, 1fr);
}
</style>
