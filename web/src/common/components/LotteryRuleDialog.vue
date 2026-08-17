<script setup lang="ts">
/**
 * 玩法规则 弹窗（共享组件）
 * 奖级对照表由前端按库内规则数据自绘：实心球=必须命中的号码，空心球=不限
 * 数据源为每日抓取的官网玩法规则条文（库内无生效版本时展示内置兜底规则）
 * 用法：const ref = ref(); ref.value?.open(type)
 */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { fullscreenElement } from '@/common/utils/fullscreen'
import {
  getLotteryRuleView, reviewLotteryRule, crawlLotteryRule,
  type LotteryRuleView, type LotteryGradeRule, type LotteryHitCond,
} from '@/common/lottery'

const visible = ref(false)
const loading = ref(false)
const data = ref<LotteryRuleView | null>(null)
/** 当前彩种（刷新与抓取用） */
const lotteryType = ref('')
/** 条文全文折叠面板展开项 */
const activeText = ref<string[]>([])
const reviewing = ref(false)
const crawling = ref(false)

const grades = computed<LotteryGradeRule[]>(() => data.value?.current?.grades ?? [])

async function open(type: string) {
  lotteryType.value = type
  visible.value = true
  activeText.value = []
  await load()
}

async function load() {
  loading.value = true
  try {
    data.value = await getLotteryRuleView(lotteryType.value)
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '获取玩法规则失败', appendTo: fullscreenElement() })
  } finally {
    loading.value = false
  }
}

/** 审核待审版本：启用后判奖立即改用新规则 */
async function review(approve: boolean) {
  const id = data.value?.pending?.id
  if (!id) return
  reviewing.value = true
  try {
    await reviewLotteryRule(id, approve)
    ElMessage.success({ message: approve ? '新版本已启用' : '已驳回', appendTo: fullscreenElement() })
    await load()
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '操作失败', appendTo: fullscreenElement() })
  } finally {
    reviewing.value = false
  }
}

/** 立即抓取官网条文（后台任务，抓到差异会转为待审版本） */
async function crawlNow() {
  crawling.value = true
  try {
    await crawlLotteryRule(lotteryType.value)
    ElMessage.success({ message: '已提交抓取任务，稍后刷新查看', appendTo: fullscreenElement() })
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '提交抓取任务失败', appendTo: fullscreenElement() })
  } finally {
    crawling.value = false
  }
}

/** 圆点序列：前 hit 个实心（必须命中），其余空心（不限） */
function dots(total: number, hit: number): boolean[] {
  return Array.from({ length: total }, (_, i) => i < hit)
}

function frontDots(cond: LotteryHitCond): boolean[] {
  return dots(data.value?.frontTotal ?? 0, cond.front)
}

function backDots(cond: LotteryHitCond): boolean[] {
  return dots(data.value?.backTotal ?? 0, cond.back)
}

/** 单注奖金展示：固定奖金优先，其次条文原文，浮动奖级注明按官方当期公布 */
function moneyText(g: LotteryGradeRule): string {
  if (g.fixedMoney != null) return `¥${g.fixedMoney.toLocaleString('zh-CN')}`
  if (g.moneyText) return g.moneyText
  return '按官方当期公布'
}

function fmtTime(t?: string | null): string {
  return t ? t.replace('T', ' ').substring(0, 19) : '—'
}

defineExpose({ open })
</script>

<template>
  <CommonDialog
    v-model="visible"
    :title="`玩法规则 · ${data?.typeName ?? ''}`"
    width="720px"
    destroy-on-close
  >
    <div v-loading="loading">
      <template v-if="data">
        <!-- 库内暂无生效版本：判奖走内置兜底规则 -->
        <el-alert
          v-if="data.usingDefault"
          type="info"
          :closable="false"
          class="rule-alert"
          title="当前展示的是内置规则（尚未抓到官网条文或抓取版本待审），判奖同样按此规则执行"
        />
        <!-- 官网条文有变动：需人工确认后才切换判奖规则 -->
        <el-alert v-if="data.pending" type="warning" :closable="false" class="rule-alert">
          <template #title>
            官网条文有更新（第 {{ data.pending.version }} 版，抓取于 {{ fmtTime(data.pending.crawledAt) }}），确认后才会用于判奖
          </template>
          <div class="rule-pending-body">
            <div v-if="data.pending.remark" class="rule-pending-remark">{{ data.pending.remark }}</div>
            <div class="rule-pending-ops">
              <el-button type="primary" size="small" :loading="reviewing" @click="review(true)">启用新版本</el-button>
              <el-button size="small" :loading="reviewing" @click="review(false)">驳回</el-button>
            </div>
          </div>
        </el-alert>

        <!-- 奖级对照表：池选型自绘号码球，位置型展示条文原文 -->
        <el-table :data="grades" size="small" border class="rule-table">
          <el-table-column prop="grade" label="奖级" width="96" align="center" />
          <el-table-column label="中奖条件" min-width="240">
            <template #default="{ row }">
              <template v-if="!data!.positional && (row as LotteryGradeRule).conds.length > 0">
                <div
                  v-for="(cond, ci) in (row as LotteryGradeRule).conds"
                  :key="'c' + ci"
                  class="rule-cond"
                >
                  <span v-if="ci > 0" class="rule-or">或</span>
                  <span
                    v-for="(on, i) in frontDots(cond)"
                    :key="'f' + i"
                    class="rule-dot front-dot"
                    :class="{ off: !on }"
                  />
                  <template v-if="data!.backTotal > 0">
                    <span class="rule-plus">+</span>
                    <span
                      v-for="(on, i) in backDots(cond)"
                      :key="'b' + i"
                      class="rule-dot back-dot"
                      :class="{ off: !on }"
                    />
                  </template>
                </div>
              </template>
              <span v-else class="rule-cond-text">{{ (row as LotteryGradeRule).conditionText || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="单注奖金" width="180" align="center">
            <template #default="{ row }">
              <span :class="{ 'rule-money-float': (row as LotteryGradeRule).fixedMoney == null }">
                {{ moneyText(row as LotteryGradeRule) }}
              </span>
            </template>
          </el-table-column>
        </el-table>

        <!-- 图例：与官方奖级对照表口径一致 -->
        <div v-if="!data.positional" class="rule-legend">
          <span class="rule-dot front-dot" /><span class="rule-legend-text">{{ data.frontLabel }}命中</span>
          <span class="rule-dot front-dot off" /><span class="rule-legend-text">{{ data.frontLabel }}不限</span>
          <template v-if="data.backTotal > 0">
            <span class="rule-dot back-dot" /><span class="rule-legend-text">{{ data.backLabel }}命中</span>
            <span class="rule-dot back-dot off" /><span class="rule-legend-text">{{ data.backLabel }}不限</span>
          </template>
        </div>

        <div class="rule-meta">
          <span>版本：第 {{ data.current?.version ?? 0 }} 版（{{ data.current?.statusText }}）</span>
          <span>抓取时间：{{ fmtTime(data.current?.crawledAt) }}</span>
          <a v-if="data.current?.sourceUrl" :href="data.current.sourceUrl" target="_blank" class="rule-link">官网条文</a>
        </div>

        <!-- 条文全文：默认收起，供与对照表逐条核对 -->
        <el-collapse v-if="data.current?.ruleText" v-model="activeText" class="rule-collapse">
          <el-collapse-item title="官网玩法规则条文全文" name="text">
            <pre class="rule-text">{{ data.current.ruleText }}</pre>
          </el-collapse-item>
        </el-collapse>
      </template>
    </div>

    <template #footer>
      <el-button :loading="crawling" @click="crawlNow">立即抓取</el-button>
      <el-button @click="load">刷新</el-button>
      <el-button type="primary" @click="visible = false">关闭</el-button>
    </template>
  </CommonDialog>
</template>

<style scoped>
.rule-alert {
  margin-bottom: 12px;
}
.rule-pending-body {
  margin-top: 6px;
}
.rule-pending-remark {
  font-size: 12px;
  line-height: 1.6;
  color: #606266;
  margin-bottom: 8px;
  white-space: pre-wrap;
}
.rule-pending-ops {
  display: flex;
  gap: 8px;
}
.rule-table {
  width: 100%;
}
.rule-cond {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  padding: 2px 0;
}
.rule-or {
  font-size: 12px;
  color: #909399;
  margin-right: 4px;
}
.rule-dot {
  display: inline-block;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  flex-shrink: 0;
}
.rule-dot.front-dot {
  background: #e6393a;
  border: 1px solid #e6393a;
}
.rule-dot.back-dot {
  background: #2563eb;
  border: 1px solid #2563eb;
}
.rule-dot.off {
  background: #fff;
}
.rule-plus {
  color: #c0c4cc;
  font-size: 13px;
  margin: 0 2px;
  flex-shrink: 0;
}
.rule-cond-text {
  font-size: 12px;
  line-height: 1.6;
  color: #606266;
}
.rule-money-float {
  color: #909399;
  font-size: 12px;
}
.rule-legend {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-top: 10px;
  font-size: 12px;
  color: #909399;
}
.rule-legend-text {
  margin-right: 10px;
}
.rule-meta {
  display: flex;
  align-items: center;
  gap: 20px;
  flex-wrap: wrap;
  margin-top: 10px;
  font-size: 12px;
  color: #909399;
}
.rule-link {
  color: #409eff;
  text-decoration: none;
}
.rule-collapse {
  margin-top: 6px;
}
.rule-text {
  margin: 0;
  font-size: 12px;
  line-height: 1.7;
  color: #606266;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: inherit;
}
</style>
