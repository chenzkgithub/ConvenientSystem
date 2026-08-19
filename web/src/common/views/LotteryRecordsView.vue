<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { httpGet } from '@/api/request'
import { formatDate, todayYmd } from '@/common/formatDate'
import { useAuthStore } from '@/common/stores/auth'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import LotteryPickPanel from '@/common/components/LotteryPickPanel.vue'
import LotteryIssueVerifyDialog from '@/common/components/LotteryIssueVerifyDialog.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import {
  LOTTERY_TABS, isPositional, fmtNumber, verifyLotteryBet,
  type LotteryRecordItem, type LotteryVerifyResult, type LotteryType,
  type LotteryPrizeGrade,
} from '@/common/lottery'

// ── 彩种页签（按菜单权限过滤）──
const auth = useAuthStore()
// 彩种 → 菜单 Name（与后端邮件过滤、内置菜单口径一致）
const TYPE_MENU_CODE: Record<LotteryType, string> = {
  SSQ: 'lottery-ssq', DLT: 'lottery', PL5: 'lottery-pl5', FC3D: 'lottery-fc3d',
}
// 仅展示当前用户有菜单权限的彩种页签；默认选中第一个有权限的彩种
const visibleTabs = computed(() => LOTTERY_TABS.filter(t => auth.menuCodes.includes(TYPE_MENU_CODE[t.code])))
const activeType = ref<LotteryType>(visibleTabs.value[0]?.code ?? 'SSQ')
const positional = computed(() => isPositional(activeType.value))
const tabName = computed(() => LOTTERY_TABS.find(t => t.code === activeType.value)?.name ?? '')

// ── 选号记录列表（分页） ──
const records = ref<LotteryRecordItem[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(20)
const loading = ref(false)
const sortField = ref('')
const sortOrder = ref<'ascending' | 'descending' | null>(null)
// 筛选日期默认选中今天（可清空）
const filterDate = ref(todayYmd())

const columns = computed<DataTableColumn<LotteryRecordItem>[]>(() => {
  const cols: DataTableColumn<LotteryRecordItem>[] = [
    { type: 'index', label: '#', width: 50, align: 'center' },
    { prop: 'issueNumber', label: '期号', width: 110, align: 'center',
      formatter: (row) => row.issueNumber ?? '—',
      sortable: 'custom',
    },
    {
      prop: 'drawDate', label: '开奖日期', width: 110, align: 'center',
      formatter: (row) => row.drawDate ? formatDate(row.drawDate).slice(0, 10) : '—',
      sortable: 'custom',
    },
    { prop: 'createdAt', label: '选号时间', width: 170, formatter: (row) => formatDate(row.createdAt), sortable: 'custom' },
    { prop: 'front', label: positional.value ? '号码' : '前区', minWidth: positional.value ? 150 : 220, custom: true, sortable: 'custom' },
  ]
  if (!positional.value) {
    cols.push({ prop: 'back', label: '后区', width: 120, custom: true, sortable: 'custom' })
  }
  return cols
})

async function load() {
  loading.value = true
  try {
    const params: Record<string, unknown> = { type: activeType.value, page: page.value, size: size.value }
    if (filterDate.value) params.date = filterDate.value
    if (sortField.value) {
      params.sortField = sortField.value
      params.sortOrder = sortOrder.value === 'ascending' ? 'asc' : 'desc'
    }
    const res = await httpGet<{ total: number; list: LotteryRecordItem[] }>('/api/Common/LotteryRecord/List', params)
    records.value = res.list
    total.value = res.total
  } catch {
    records.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

function query() {
  page.value = 1
  load()
}

// 切换彩种：重置分页重新加载
watch(activeType, () => {
  page.value = 1
  load()
})

onMounted(() => load())

function onSortChange(info: { prop: string | null; order: 'ascending' | 'descending' | null }) {
  sortField.value = info.prop ?? ''
  sortOrder.value = info.order
  page.value = 1
  load()
}

/** 号码球文本：位置型不补零 */
function fmt(n: number): string {
  return fmtNumber(positional.value, n)
}

/** 验证结果弹窗内的号码球文本（按开奖彩种口径） */
function fmtVerify(n: number): string {
  return fmtNumber(isPositional(activeType.value), n)
}

// ── 中奖验证 ──
const verifyVisible = ref(false)
const verifying = ref(false)
const verifyData = ref<LotteryVerifyResult | null>(null)
// 保留被验证的记录：接口只返回本注号码文本，逐球高亮命中需要原始号码数组
const verifyRow = ref<LotteryRecordItem | null>(null)

async function openVerify(row: LotteryRecordItem) {
  verifying.value = true
  try {
    verifyData.value = await verifyLotteryBet(row.id)
    verifyRow.value = row
    verifyVisible.value = true
  } catch {
    ElMessage.error('验证失败，请稍后重试')
  } finally {
    verifying.value = false
  }
}

/** 前区第 i 位号码是否命中：位置型按位比对，池选型看号码是否在命中集合内 */
function isFrontHit(n: number, i: number): boolean {
  const h = verifyData.value?.hit
  if (!h) return false
  return positional.value ? h.positionHits[i] === true : h.frontHits.includes(n)
}

/** 后区号码是否命中（仅池选型有后区） */
function isBackHit(n: number): boolean {
  const h = verifyData.value?.hit
  return !!h && h.backHits.includes(n)
}

/** 全国中奖明细表：本注所中奖级那一行加底色突出 */
function gradeRowClass({ row }: { row: LotteryPrizeGrade }): string {
  const d = verifyData.value
  return d?.isWin && d.matchedGrade && row.grade === d.matchedGrade ? 'grade-hit-row' : ''
}

/** 金额千分位格式化 */
function fmtMoney(n: number): string {
  return n.toLocaleString('zh-CN')
}

// ── 整期批量验奖（表头按钮：一次验完当期全部选号，弹窗与首页共用同一组件）──
const issueDialogRef = ref<InstanceType<typeof LotteryIssueVerifyDialog>>()
const issueVerifying = ref(false)

async function openIssueVerify() {
  issueVerifying.value = true
  try {
    // 跟随列表筛选的开奖日期；筛选清空时后端取最新一期
    await issueDialogRef.value?.open(activeType.value, filterDate.value || undefined)
  } finally {
    issueVerifying.value = false
  }
}

// ── 选号弹窗：全屏弹出独立选号组件（destroy-on-close 保证每次打开状态干净） ──
const pickVisible = ref(false)
function openPick() {
  pickVisible.value = true
}
function onPickClosed() {
  // 弹窗内保存过选号后关闭，刷新列表保持同步
  load()
}
</script>

<template>
  <div class="records-page">
    <el-tabs v-model="activeType" class="records-tabs">
      <el-tab-pane v-for="t in visibleTabs" :key="t.code" :label="t.name" :name="t.code" />
    </el-tabs>

    <div class="records-table">
      <CommonDataTable
        show-refresh
        show-column-toggle
        table-key="lottery-records"
        @load="load"
        @sort-change="onSortChange"
        v-model:page="page"
        v-model:pageSize="size"
        :columns="columns"
        :data="records"
        :loading="loading || verifying"
        :total="total"
        :actions-width="90"
        :page-sizes="[10, 20, 50]"
        compact
        pagination-layout="total, sizes, prev, pager, next"
      >
        <template #filters>
          <el-date-picker
            v-model="filterDate"
            type="date"
            placeholder="按开奖日期筛选"
            value-format="YYYY-MM-DD"
            style="width: 160px"
            clearable
            @change="query"
          />
        </template>

        <template #toolbar>
          <el-button type="primary" size="small" @click="query">查询</el-button>
          <el-button type="primary" size="small" @click="openPick">选号</el-button>
          <el-button v-if="$has('lottery-records:verify-issue')" type="danger" size="small" :loading="issueVerifying" @click="openIssueVerify">验证奖金</el-button>
        </template>

        <template #cell-front="{ row }">
          <div class="balls-cell">
            <span v-for="(n, i) in row.front" :key="'f' + i" class="mini-ball front-mini">{{ fmt(n) }}</span>
          </div>
        </template>

        <template #cell-back="{ row }">
          <div class="balls-cell">
            <span v-for="(n, i) in row.back" :key="'b' + i" class="mini-ball back-mini">{{ fmt(n) }}</span>
          </div>
        </template>

        <template #actions="{ row }">
          <el-button v-if="$has('lottery-records:verify')" link type="primary" size="small" @click="openVerify(row as LotteryRecordItem)">验证</el-button>
        </template>

        <template #empty>暂无选号记录</template>
      </CommonDataTable>
    </div>

    <!-- 中奖验证弹窗 -->
    <CommonDialog
      v-model="verifyVisible"
      :title="`中奖验证 · ${tabName}`"
      width="640px"
      destroy-on-close
    >
      <template v-if="verifyData">
        <!-- 开奖号码 -->
        <div v-if="verifyData.hasDraw" class="verify-block">
          <div class="verify-label">
            第 {{ verifyData.issueNumber }} 期开奖号码
            <span class="verify-sub">{{ formatDate(verifyData.drawDate).slice(0, 10) }}</span>
          </div>
          <div class="balls-cell">
            <span v-for="(n, i) in verifyData.drawFront" :key="'df' + i" class="mini-ball front-mini">{{ fmtVerify(n) }}</span>
            <template v-if="verifyData.drawBack.length > 0">
              <span class="bet-sep">+</span>
              <span v-for="(n, i) in verifyData.drawBack" :key="'db' + i" class="mini-ball back-mini">{{ fmtVerify(n) }}</span>
            </template>
          </div>
        </div>

        <!-- 本注结果：逐球高亮命中 + 命中统计 + 税前/税后奖金 -->
        <div class="verify-block">
          <div class="verify-label">
            您的选号
            <span v-if="verifyData.hit" class="verify-sub">{{ verifyData.hit.hitSummary }}</span>
          </div>
          <div class="verify-pick">
            <!-- 有开奖时逐球比对：命中球加金边突出，未命中球去色淡化 -->
            <div v-if="verifyData.hasDraw && verifyRow" class="balls-cell">
              <span
                v-for="(n, i) in verifyRow.front"
                :key="'pf' + i"
                class="mini-ball front-mini"
                :class="isFrontHit(n, i) ? 'ball-hit' : 'ball-miss'"
              >{{ fmtVerify(n) }}</span>
              <template v-if="verifyRow.back.length > 0">
                <span class="bet-sep">+</span>
                <span
                  v-for="(n, i) in verifyRow.back"
                  :key="'pb' + i"
                  class="mini-ball back-mini"
                  :class="isBackHit(n) ? 'ball-hit' : 'ball-miss'"
                >{{ fmtVerify(n) }}</span>
              </template>
            </div>
            <span v-else class="verify-pick-text">{{ verifyData.pick }}</span>

            <el-tag v-if="!verifyData.hasDraw" type="warning" effect="light">未开奖</el-tag>
            <el-tag v-else-if="verifyData.isWin" type="danger" effect="dark">{{ verifyData.prize }}</el-tag>
            <el-tag v-else type="info" effect="plain">未中奖</el-tag>
          </div>

          <!-- 奖金明细：单注超 1 万需缴 20% 偶然所得个税，此时才分列税前/个税/税后 -->
          <div v-if="verifyData.isWin" class="verify-money">
            <template v-if="verifyData.money != null">
              <span class="money-main">奖金 ¥{{ fmtMoney(verifyData.money) }}</span>
              <template v-if="verifyData.tax != null && verifyData.tax > 0">
                <span class="money-tax">个税（20%）−¥{{ fmtMoney(verifyData.tax) }}</span>
                <span class="money-net">税后实得 ¥{{ fmtMoney(verifyData.moneyAfterTax ?? 0) }}</span>
              </template>
              <span v-else class="money-note">单注不超 1 万元，免征个税</span>
              <span v-if="verifyData.gradeCount != null" class="money-note">
                全国同奖级 {{ fmtMoney(verifyData.gradeCount) }} 注
              </span>
            </template>
            <span v-else class="money-note">该期官方未提供本奖级奖金数据，金额暂无法测算</span>
          </div>
        </div>

        <!-- 官网通告：全国中奖情况 -->
        <div class="verify-block">
          <div class="verify-label">官网通告 · 全国中奖情况</div>
          <el-table v-if="verifyData.grades.length > 0" :data="verifyData.grades" size="small" border
            :row-class-name="gradeRowClass">
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
          <div v-if="verifyData.salesAmount != null || verifyData.poolBalance != null" class="verify-meta">
            <span v-if="verifyData.salesAmount != null">当期销量：¥{{ fmtMoney(verifyData.salesAmount) }}</span>
            <span v-if="verifyData.poolBalance != null">奖池滚存：¥{{ fmtMoney(verifyData.poolBalance) }}</span>
          </div>
          <!-- 一等奖中奖地区（福彩双色球官网通告口径） -->
          <div v-if="verifyData.prizeArea" class="verify-area">
            <span class="verify-area-label">一等奖中奖地区：</span>{{ verifyData.prizeArea }}
          </div>
          <!-- 官方通告 PDF（体彩大乐透/排列五，含中奖地区等完整通告） -->
          <a v-if="verifyData.noticeUrl" :href="verifyData.noticeUrl" target="_blank" rel="noopener"
            class="verify-pdf">查看官网通告原文（PDF）</a>
        </div>
      </template>
    </CommonDialog>

    <!-- 整期批量验奖弹窗（共享组件，与首页彩种卡片验奖同一口径） -->
    <LotteryIssueVerifyDialog ref="issueDialogRef" />

    <!-- 选号弹窗：全屏弹出独立选号组件 -->
    <CommonDialog
      v-model="pickVisible"
      title="选号"
      fullscreen
      destroy-on-close
      class="pick-dialog"
      @closed="onPickClosed"
    >
      <LotteryPickPanel :type="activeType" />
    </CommonDialog>
  </div>
</template>

<style scoped>
.records-page {
  padding: 12px;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.records-tabs {
  flex-shrink: 0;
}
.records-tabs :deep(.el-tabs__header) {
  margin-bottom: 8px;
}

.records-table {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

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

/* 验证弹窗 */
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
.verify-pick {
  display: flex;
  align-items: center;
  gap: 12px;
}
.verify-pick-text {
  font-size: 16px;
  font-weight: 700;
  color: #303133;
  letter-spacing: 1px;
}

/* 奖金明细行：税前金额突出，个税与税后实得分开展示 */
.verify-money {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px 14px;
  margin-top: 8px;
  font-size: 13px;
}
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

/* 全国中奖明细：本注所中奖级行加底色（el-table 行类需 deep 穿透 scoped） */
:deep(.grade-hit-row) {
  background: #fff7e6 !important;
  font-weight: 700;
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

/* 选号全屏弹窗：正文撑满剩余高度，选号组件内部自行布局 */
.pick-dialog {
  display: flex;
  flex-direction: column;
}
.pick-dialog :deep(.el-dialog__body) {
  flex: 1;
  min-height: 0;
  overflow: hidden;
  padding: 12px 16px;
}
</style>
