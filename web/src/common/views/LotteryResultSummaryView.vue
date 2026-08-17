<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { httpGet } from '@/api/request'

interface LotteryHitResult {
  prize: string
  isWin: boolean
  frontHits: number[]
  backHits: number[]
  positionHits: boolean[]
  frontHitCount: number
  backHitCount: number
}

interface LotteryPrizeGrade {
  grade: string
  count?: number
  money?: number
}

interface LotterySummaryRecord {
  userName: string
  front: number[]
  back: number[]
  createdAt: string
  drawDate?: string
  hit: LotteryHitResult
  prize: string
  money?: number
  tax?: number
  net?: number
}

interface LotterySummaryDraw {
  type: string
  typeName: string
  color: string
  positional: boolean
  issueNumber: string
  drawDate: string
  front: number[]
  back: number[]
  grades: LotteryPrizeGrade[]
  salesAmount?: number
  poolBalance?: number
  prizeArea?: string
  noticeUrl?: string
  records: LotterySummaryRecord[]
}

interface LotteryResultSummary {
  date: string
  title: string
  subtitle: string
  isLatestFallback: boolean
  draws: LotterySummaryDraw[]
}

const route = useRoute()
const loading = ref(false)
const summary = ref<LotteryResultSummary | null>(null)

const dateParam = computed(() => route.query.date as string | undefined)

function formatDate(d: string | undefined) {
  if (!d) return ''
  const date = new Date(d)
  return isNaN(date.getTime()) ? d : date.toLocaleDateString('zh-CN')
}

function formatMoney(n: number | undefined) {
  if (n === undefined || n === null) return '—'
  return `¥${n.toLocaleString('zh-CN')}`
}

function padNum(n: number) {
  return n.toString().padStart(2, '0')
}

function renderPick(record: LotterySummaryRecord, draw: LotterySummaryDraw) {
  const frontSpans = record.front.map((n, i) => {
    const isHit = draw.positional
      ? record.hit.positionHits[i]
      : record.hit.frontHits.includes(n)
    return `<span class="ball-text" style="color:${isHit ? '#f56c6c' : '#c0c4cc'};font-weight:bold">${draw.positional ? n : padNum(n)}</span>`
  })
  let html = frontSpans.join(' ')
  if (!draw.positional && record.back.length > 0) {
    const backSpans = record.back.map(n => {
      const isHit = record.hit.backHits.includes(n)
      return `<span class="ball-text" style="color:${isHit ? '#409eff' : '#c0c4cc'};font-weight:bold">${padNum(n)}</span>`
    })
    html += ` <span style="color:#c0c4cc">+</span> ${backSpans.join(' ')}`
  }
  return html
}

function renderDrawBalls(draw: LotterySummaryDraw) {
  const frontSpans = draw.front.map(n => `<span class="ball" style="background:#e6393a">${draw.positional ? n : padNum(n)}</span>`)
  let html = frontSpans.join('')
  if (!draw.positional && draw.back.length > 0) {
    const backSpans = draw.back.map(n => `<span class="ball" style="background:#2563eb">${padNum(n)}</span>`)
    html += '<span style="color:#c0c4cc;margin:0 6px">+</span>' + backSpans.join('')
  }
  return html
}

async function load() {
  loading.value = true
  try {
    const params: Record<string, string> = {}
    if (dateParam.value) params.date = dateParam.value
    summary.value = await httpGet<LotteryResultSummary>('/api/Common/LotteryResult/GetSummary', params)
  } catch (e) {
    ElMessage.error('加载开奖汇总失败')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="summary-page">
    <div v-if="loading" class="loading">加载中…</div>
    <template v-else-if="summary">
      <div class="header">
        <h1 class="title">{{ summary.title }}</h1>
        <p class="subtitle">{{ summary.subtitle }}</p>
        <el-alert v-if="summary.isLatestFallback" type="info" :closable="false" show-icon>
          今日无开奖，以下为各彩种最新一期开奖结果。
        </el-alert>
      </div>

      <div v-if="!summary.draws.length" class="empty">暂无开奖数据</div>

      <div v-for="draw in summary.draws" :key="draw.type + draw.issueNumber" class="draw-card">
        <div class="draw-header">
          <span class="type-badge" :style="{ background: draw.color }">{{ draw.typeName }}</span>
          <span class="issue">第 {{ draw.issueNumber }} 期</span>
          <span class="draw-date">开奖日期 {{ formatDate(draw.drawDate) }}</span>
        </div>

        <div class="draw-numbers">
          <span class="label">开奖号码：</span>
          <span class="balls" v-html="renderDrawBalls(draw)" />
        </div>

        <div class="section-title" :style="{ borderLeftColor: draw.color }">官网通告 · 全国中奖情况</div>
        <div v-if="draw.grades.length" class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>奖级</th>
                <th>全国中奖注数</th>
                <th>单注奖金</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(g, idx) in draw.grades" :key="idx">
                <td>{{ g.grade }}</td>
                <td>{{ g.count !== undefined ? g.count.toLocaleString('zh-CN') : '—' }}</td>
                <td>{{ formatMoney(g.money) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="draw.salesAmount || draw.poolBalance" class="meta">
          <span v-if="draw.salesAmount">当期销量：{{ formatMoney(draw.salesAmount) }}</span>
          <span v-if="draw.poolBalance">奖池滚存：{{ formatMoney(draw.poolBalance) }}</span>
        </div>
        <div v-if="draw.prizeArea" class="prize-area">
          <b>一等奖中奖地区：</b>{{ draw.prizeArea }}
        </div>
        <div v-if="draw.noticeUrl" class="notice-link">
          <a :href="draw.noticeUrl" target="_blank" rel="noopener">查看官网通告原文（PDF）</a>
        </div>

        <div class="section-title" :style="{ borderLeftColor: draw.color }">本期选号及中奖结果</div>
        <div v-if="draw.records.length" class="table-wrap">
          <table class="data-table record-table">
            <thead>
              <tr>
                <th>用户</th>
                <th>选号时间</th>
                <th>选号</th>
                <th>命中</th>
                <th>中奖结果</th>
                <th>奖金</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(rec, idx) in draw.records" :key="idx">
                <td>{{ rec.userName }}</td>
                <td>{{ formatDate(rec.drawDate) || formatDate(rec.createdAt) }}</td>
                <td class="pick-cell" v-html="renderPick(rec, draw)" />
                <td>
                  {{ draw.positional ? `${rec.hit.frontHitCount} 位` : `${rec.hit.frontHitCount}+${rec.hit.backHitCount}` }}
                </td>
                <td :class="['prize-cell', rec.hit.isWin ? 'win' : 'lose']">{{ rec.prize }}</td>
                <td>
                  <template v-if="rec.money !== undefined && rec.money !== null">
                    <span class="money">{{ formatMoney(rec.money) }}</span>
                    <div v-if="rec.tax && rec.net !== undefined" class="tax">税后 {{ formatMoney(rec.net) }}</div>
                  </template>
                  <template v-else>—</template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="empty-section">本期暂无选号记录</div>
      </div>
    </template>
    <div v-else class="loading">无法加载开奖汇总</div>
  </div>
</template>

<style scoped>
.summary-page {
  max-width: 900px;
  margin: 0 auto;
  padding: 16px;
  font-family: 'Microsoft YaHei', 'PingFang SC', sans-serif;
  color: #303133;
  background: #f2f4f7;
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}
.header {
  background: #fff;
  border-radius: 8px;
  padding: 20px;
  margin-bottom: 16px;
  text-align: center;
}
.title {
  margin: 0 0 8px;
  font-size: 20px;
  color: #d63031;
}
.subtitle {
  margin: 0 0 12px;
  color: #606266;
  font-size: 13px;
}
.loading, .empty {
  text-align: center;
  padding: 40px 0;
  color: #909399;
}
.draw-card {
  background: #fff;
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
}
.draw-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
}
.type-badge {
  display: inline-block;
  color: #fff;
  font-weight: bold;
  padding: 3px 12px;
  border-radius: 4px;
  font-size: 14px;
}
.issue {
  font-weight: bold;
  font-size: 15px;
}
.draw-date {
  color: #909399;
  font-size: 13px;
}
.draw-numbers {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
}
.label {
  font-weight: bold;
}
.balls {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
}
.balls :deep(.ball) {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  color: #fff;
  font-weight: 700;
  font-size: 13px;
}
.section-title {
  font-weight: bold;
  border-left: 3px solid #d63031;
  padding-left: 8px;
  margin: 16px 0 10px;
}
.table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}
.data-table {
  border-collapse: collapse;
  width: 100%;
  min-width: 600px;
  font-size: 13px;
  border: 1px solid #e4e7ed;
}
.data-table th, .data-table td {
  border: 1px solid #e4e7ed;
  padding: 8px;
  text-align: center;
  white-space: nowrap;
}
.data-table th {
  background: #f5f7fa;
  font-weight: bold;
}
.data-table tr:nth-child(even) {
  background: #fafafa;
}
.record-table .pick-cell {
  font-family: monospace;
  letter-spacing: 0;
}
.prize-cell.win {
  color: #f56c6c;
  font-weight: bold;
}
.prize-cell.lose {
  color: #909399;
}
.money {
  color: #f56c6c;
  font-weight: bold;
}
.tax {
  color: #909399;
  font-size: 12px;
}
.meta {
  color: #606266;
  font-size: 13px;
  margin-top: 8px;
}
.meta span {
  margin-right: 16px;
}
.prize-area {
  margin-top: 8px;
  color: #606266;
  font-size: 13px;
}
.prize-area b {
  color: #f56c6c;
}
.notice-link {
  margin-top: 8px;
}
.notice-link a {
  color: #409eff;
  font-size: 13px;
}
.empty-section {
  color: #909399;
  font-size: 13px;
  padding: 8px 0;
}
</style>
