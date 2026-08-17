<script setup lang="ts">
/**
 * 当期验奖 弹窗（共享组件）
 * 一次验完指定开奖期的全部选号：逐注命中高亮 + 税前/个税/税后奖金 + 全国中奖明细
 * 用法：const ref = ref(); ref.value?.open(type, date?)  date 缺省时后端取最新一期
 */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { formatDate } from '@/common/formatDate'
import { fullscreenElement } from '@/common/utils/fullscreen'
import {
  LOTTERY_TABS, isPositional, fmtNumber, verifyLotteryIssue,
  type LotteryType, type LotteryIssueVerifyResult, type LotteryIssueBet, type LotteryPrizeGrade,
} from '@/common/lottery'

const visible = ref(false)
const loading = ref(false)
const issueData = ref<LotteryIssueVerifyResult | null>(null)
const lotteryType = ref<LotteryType>('SSQ')

const positional = computed(() => isPositional(lotteryType.value))
const typeName = computed(() => LOTTERY_TABS.find(t => t.code === lotteryType.value)?.name ?? '')

/**
 * 打开并验奖：date（yyyy-MM-dd）为开奖日，缺省时验最新一期
 * 数据取回后才弹窗，避免弹出空白框
 */
async function open(type: LotteryType, date?: string) {
  loading.value = true
  try {
    lotteryType.value = type
    issueData.value = await verifyLotteryIssue(type, date)
    visible.value = true
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '验奖失败，请稍后重试', appendTo: fullscreenElement() })
  } finally {
    loading.value = false
  }
}

/** 号码球文本：位置型不补零 */
function fmt(n: number): string {
  return fmtNumber(positional.value, n)
}

/** 金额千分位格式化 */
function fmtMoney(n: number): string {
  return n.toLocaleString('zh-CN')
}

/** 某注前区第 i 位号码是否命中：位置型按位比对，池选型看号码是否在命中集合内 */
function isBetFrontHit(bet: LotteryIssueBet, n: number, i: number): boolean {
  const h = bet.hit
  if (!h) return false
  return positional.value ? h.positionHits[i] === true : h.frontHits.includes(n)
}

/** 某注后区号码是否命中（仅池选型有后区） */
function isBetBackHit(bet: LotteryIssueBet, n: number): boolean {
  return !!bet.hit && bet.hit.backHits.includes(n)
}

/** 命中数紧凑文本（池选型 3+1、位置型 2 位） */
function hitText(bet: LotteryIssueBet): string {
  const h = bet.hit
  if (!h) return '—'
  return positional.value ? `${h.frontHitCount} 位` : `${h.frontHitCount}+${h.backHitCount}`
}

/** 中奖的那几注整行加底色 */
function betRowClass({ row }: { row: LotteryIssueBet }): string {
  return row.isWin ? 'bet-win-row' : ''
}

/** 本期命中过的奖级行加底色（可能命中多个奖级） */
function issueGradeRowClass({ row }: { row: LotteryPrizeGrade }): string {
  const d = issueData.value
  return d?.bets.some(b => b.isWin && b.matchedGrade === row.grade) ? 'grade-hit-row' : ''
}

defineExpose({ open, loading })
</script>

<template>
  <CommonDialog
    v-model="visible"
    :title="'当期验奖 · ' + typeName"
    width="860px"
    destroy-on-close
  >
    <template v-if="issueData">
      <el-alert
        v-if="!issueData.hasDraw"
        type="warning"
        :closable="false"
        title="该日期尚未开奖，或历史开奖数据未采集，无法验奖"
      />
      <template v-else>
        <!-- 开奖号码 -->
        <div class="verify-block">
          <div class="verify-label">
            第 {{ issueData.issueNumber }} 期开奖号码
            <span class="verify-sub">{{ formatDate(issueData.drawDate).slice(0, 10) }}</span>
          </div>
          <div class="balls-cell">
            <span v-for="(n, i) in issueData.drawFront" :key="'idf' + i" class="mini-ball front-mini">{{ fmt(n) }}</span>
            <template v-if="issueData.drawBack.length > 0">
              <span class="bet-sep">+</span>
              <span v-for="(n, i) in issueData.drawBack" :key="'idb' + i" class="mini-ball back-mini">{{ fmt(n) }}</span>
            </template>
          </div>
        </div>

        <!-- 汇总条：注数 / 中奖注数 / 合计奖金（税前·个税·税后） -->
        <div class="issue-summary" :class="issueData.winCount > 0 ? 'summary-win' : 'summary-none'">
          <template v-if="issueData.betCount === 0">本期没有选号记录</template>
          <template v-else>
            <span>本期共 <b>{{ issueData.betCount }}</b> 注</span>
            <span>中奖 <b class="win-num">{{ issueData.winCount }}</b> 注</span>
            <template v-if="issueData.winCount > 0 && issueData.moneyKnown">
              <span class="money-main">合计奖金 ¥{{ fmtMoney(issueData.totalMoney) }}</span>
              <template v-if="issueData.totalTax > 0">
                <span class="money-tax">个税 −¥{{ fmtMoney(issueData.totalTax) }}</span>
                <span class="money-net">税后实得 ¥{{ fmtMoney(issueData.totalMoneyAfterTax) }}</span>
              </template>
              <span v-else class="money-note">单注均不超 1 万元，免征个税</span>
            </template>
            <span v-else-if="issueData.winCount > 0" class="money-note">该期官方未提供奖金数据，金额暂无法测算</span>
          </template>
        </div>

        <!-- 逐注结果：命中球金边突出，未命中球去色淡化 -->
        <div v-if="issueData.bets.length > 0" class="verify-block">
          <div class="verify-label">逐注验奖结果</div>
          <el-table :data="issueData.bets" size="small" border :row-class-name="betRowClass">
            <el-table-column type="index" label="#" width="46" align="center" />
            <el-table-column :label="positional ? '号码' : '选号'" min-width="230">
              <template #default="{ row }">
                <div class="balls-cell">
                  <span
                    v-for="(n, i) in row.front"
                    :key="'bf' + i"
                    class="mini-ball front-mini"
                    :class="isBetFrontHit(row as LotteryIssueBet, n, i) ? 'ball-hit' : 'ball-miss'"
                  >{{ fmt(n) }}</span>
                  <template v-if="row.back.length > 0">
                    <span class="bet-sep">+</span>
                    <span
                      v-for="(n, i) in row.back"
                      :key="'bb' + i"
                      class="mini-ball back-mini"
                      :class="isBetBackHit(row as LotteryIssueBet, n) ? 'ball-hit' : 'ball-miss'"
                    >{{ fmt(n) }}</span>
                  </template>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="命中" width="72" align="center">
              <template #default="{ row }">{{ hitText(row as LotteryIssueBet) }}</template>
            </el-table-column>
            <el-table-column label="中奖结果" width="104" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.isWin" type="danger" effect="dark" size="small">{{ row.prize }}</el-tag>
                <span v-else class="money-note">未中奖</span>
              </template>
            </el-table-column>
            <el-table-column label="奖金（元）" width="128" align="center">
              <template #default="{ row }">
                <template v-if="row.isWin && row.money != null">
                  <div class="money-main">¥{{ fmtMoney(row.money) }}</div>
                  <div v-if="row.tax != null && row.tax > 0" class="money-tax">税后 ¥{{ fmtMoney(row.moneyAfterTax ?? 0) }}</div>
                </template>
                <span v-else class="money-note">—</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <!-- 官网通告：全国中奖情况（本期命中过的奖级行加底色） -->
        <div class="verify-block">
          <div class="verify-label">官网通告 · 全国中奖情况</div>
          <el-table v-if="issueData.grades.length > 0" :data="issueData.grades" size="small" border
            :row-class-name="issueGradeRowClass">
            <el-table-column prop="grade" label="奖级" width="120" align="center" />
            <el-table-column label="全国中奖注数" align="center">
              <template #default="{ row }">{{ row.count != null ? fmtMoney(row.count) : '—' }}</template>
            </el-table-column>
            <el-table-column label="单注奖金（元）" align="center">
              <template #default="{ row }">{{ row.money != null ? fmtMoney(row.money) : '—' }}</template>
            </el-table-column>
          </el-table>
          <el-alert
            v-else
            type="info"
            :closable="false"
            title="该期暂无全国中奖明细（历史期未采集，新开奖期将自动采集）"
          />
          <div v-if="issueData.salesAmount != null || issueData.poolBalance != null" class="verify-meta">
            <span v-if="issueData.salesAmount != null">当期销量：¥{{ fmtMoney(issueData.salesAmount) }}</span>
            <span v-if="issueData.poolBalance != null">奖池滚存：¥{{ fmtMoney(issueData.poolBalance) }}</span>
          </div>
          <div v-if="issueData.prizeArea" class="verify-area">
            <span class="verify-area-label">一等奖中奖地区：</span>{{ issueData.prizeArea }}
          </div>
          <a v-if="issueData.noticeUrl" :href="issueData.noticeUrl" target="_blank" rel="noopener"
            class="verify-pdf">查看官网通告原文（PDF）</a>
        </div>
      </template>
    </template>
  </CommonDialog>
</template>

<style scoped>
/* 号码球 */
.balls-cell {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: nowrap;
}
.mini-ball {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  font-size: 12px;
  font-weight: 700;
  color: #fff;
  flex-shrink: 0;
}
.front-mini { background: #e6393a; }
.back-mini { background: #2563eb; }
.bet-sep { color: #c0c4cc; font-size: 13px; margin: 0 2px; flex-shrink: 0; }

/* 验奖结果：命中球加金边并略放大，未命中球去色淡化，一眼看出对了哪几个 */
.ball-hit {
  box-shadow: 0 0 0 2px #f7b500, 0 1px 4px rgba(0, 0, 0, 0.2);
  transform: scale(1.08);
}
.ball-miss {
  background: #dcdfe6;
  color: #f2f3f5;
}

.verify-block {
  margin-bottom: 12px;
}
.verify-block:last-child {
  margin-bottom: 0;
}
.verify-label {
  font-size: 13px;
  font-weight: 600;
  color: #606266;
  margin-bottom: 8px;
}
.verify-sub {
  font-weight: 400;
  color: #909399;
  margin-left: 8px;
}

/* 奖金：税前金额突出，个税与税后实得分开展示 */
.money-main {
  font-size: 15px;
  font-weight: 700;
  color: #e6393a;
}
.money-tax {
  color: #909399;
}
.money-net {
  font-weight: 700;
  color: #67c23a;
}
.money-note {
  color: #909399;
}

/* 全国中奖明细：本期命中的奖级行加底色（el-table 行类需 deep 穿透 scoped） */
:deep(.grade-hit-row) {
  background: #fff7e6 !important;
  font-weight: 700;
}

/* 汇总条：有中奖时淡红底，未中奖时灰底 */
.issue-summary {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px 16px;
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 13px;
  margin-bottom: 12px;
}
.summary-win {
  background: #fef0f0;
  color: #606266;
}
.summary-none {
  background: #f4f4f5;
  color: #909399;
}
.win-num {
  font-size: 15px;
  color: #e6393a;
}

/* 逐注表格：中奖的那几注整行加淡黄底 */
:deep(.bet-win-row) {
  background: #fff7e6 !important;
}
.verify-meta {
  display: flex;
  gap: 24px;
  margin-top: 10px;
  font-size: 13px;
  color: #909399;
}
.verify-area {
  margin-top: 10px;
  font-size: 13px;
  color: #606266;
  line-height: 1.6;
}
.verify-area-label {
  font-weight: 600;
  color: #e6393a;
}
.verify-pdf {
  display: inline-block;
  margin-top: 10px;
  font-size: 13px;
  color: #409eff;
  text-decoration: none;
}
.verify-pdf:hover {
  text-decoration: underline;
}
</style>
