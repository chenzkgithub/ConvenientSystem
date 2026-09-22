<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import CommonDialog from './CommonDialog.vue'
import { LOTTERY_TABS, isPositional, fmtNumber, calculateLotteryBets } from '@/common/lottery'

/** 金额计算对话框：支持 4 个彩种 × 全部玩法的投注金额实时计算 */
const visible = defineModel<boolean>({ default: false })

// ── 彩种切换 ──
const curType = ref('SSQ')
const typeName = computed(() => LOTTERY_TABS.find(t => t.code === curType.value)?.name ?? '')
const positional = computed(() => isPositional(curType.value))

// ── 玩法 ──
type ModeOpt = { value: string; label: string }
const allModes: Record<string, ModeOpt[]> = {
  SSQ: [{ value: '单式', label: '单式' }, { value: '复式', label: '复式' }, { value: '胆拖', label: '胆拖' }],
  DLT: [{ value: '单式', label: '单式' }, { value: '复式', label: '复式' }, { value: '胆拖', label: '胆拖' }],
  PL5: [{ value: '单式', label: '单式' }, { value: '复式', label: '复式' }],
  FC3D: [{ value: '直选', label: '直选' }, { value: '复式', label: '复式' }, { value: '组选6', label: '组选6' }, { value: '组选3', label: '组选3' }],
}
const curModes = computed(() => allModes[curType.value] ?? [])
const curMode = ref('复式')

// ── 选号状态 ──
const frontSel = ref<number[]>([])
const backSel = ref<number[]>([])
const danSel = ref<number[]>([])
const trailSel = ref<number[]>([])
const posSel = ref<number[][]>([])
const backDanSel = ref<number[]>([])
const backTrailSel = ref<number[]>([])
const backDantuo = ref(false)

// ── 号码池 ──
const frontPool = computed(() => {
  if (curType.value === 'SSQ') return Array.from({ length: 33 }, (_, i) => i + 1)
  if (curType.value === 'DLT') return Array.from({ length: 35 }, (_, i) => i + 1)
  return Array.from({ length: 10 }, (_, i) => i)
})
const backPool = computed(() =>
  Array.from({ length: curType.value === 'DLT' ? 12 : 16 }, (_, i) => i + 1)
)
const posCount = computed(() => curType.value === 'PL5' ? 5 : 3)
const posLabels = computed(() => curType.value === 'PL5' ? ['万', '千', '百', '十', '个'] : ['百', '十', '个'])

const danMax = computed(() => curType.value === 'DLT' ? 4 : 5)

// ── 彩种切换重置 ──
watch(curType, () => {
  frontSel.value = []
  backSel.value = []
  danSel.value = []
  trailSel.value = []
  backDanSel.value = []
  backTrailSel.value = []
  backDantuo.value = false
  posSel.value = Array.from({ length: posCount.value }, () => [])
  const modes = curModes.value
  if (!modes.find(m => m.value === curMode.value)) curMode.value = modes[0]?.value ?? '单式'
})

// ── 选号操作 ──
function toggleIn(arr: number[], n: number, max?: number): number[] {
  if (arr.includes(n)) return arr.filter(x => x !== n)
  if (max !== undefined && arr.length >= max) return arr
  return [...arr, n]
}
function toggleDan(n: number) {
  if (danSel.value.includes(n)) { danSel.value = danSel.value.filter(x => x !== n); return }
  if (danSel.value.length >= danMax.value) return
  danSel.value = [...danSel.value, n]
}
function toggleTrail(n: number) { trailSel.value = toggleIn(trailSel.value, n) }
function toggleBackDan(n: number) {
  if (backDanSel.value.includes(n)) { backDanSel.value = backDanSel.value.filter(x => x !== n); return }
  if (backDanSel.value.length >= 1) return
  backDanSel.value = [...backDanSel.value, n]
}
function toggleBackTrail(n: number) { backTrailSel.value = toggleIn(backTrailSel.value, n) }
function togglePos(pos: number, n: number) {
  const max = curMode.value === '单式' ? 1 : undefined
  const next = [...posSel.value]
  next[pos] = toggleIn(next[pos] ?? [], n, max)
  posSel.value = next
}
function clearAll() {
  frontSel.value = []; backSel.value = []
  danSel.value = []; trailSel.value = []
  backDanSel.value = []; backTrailSel.value = []; backDantuo.value = false
  posSel.value = Array.from({ length: posCount.value }, () => [])
}

// ── 计算 ──
const result = computed(() => {
  let bets = 0
  if (positional.value) {
    bets = calculateLotteryBets(curType.value, curMode.value, { positional: posSel.value })
  } else if (curMode.value === '胆拖') {
    bets = calculateLotteryBets(curType.value, curMode.value, {
      front: danSel.value, trail: trailSel.value,
      back: backDantuo.value ? [] : backSel.value,
      backDan: backDantuo.value ? backDanSel.value : undefined,
      backTrail: backDantuo.value ? backTrailSel.value : undefined,
    })
  } else if (curMode.value === '组选6' || curMode.value === '组选3') {
    bets = calculateLotteryBets(curType.value, curMode.value, { front: frontSel.value })
  } else {
    bets = calculateLotteryBets(curType.value, curMode.value, { front: frontSel.value, back: backSel.value })
  }
  return { bets, cost: bets * 2 }
})

const formulaText = computed(() => {
  const fc = frontSel.value.length, bc = backSel.value.length
  const dc = danSel.value.length, tc = trailSel.value.length
  const pick = curType.value === 'DLT' ? 5 : 6
  const bdc = backDanSel.value.length, btc = backTrailSel.value.length
  if (curMode.value === '单式' || curMode.value === '直选') return '每注 = ¥2'
  if (curMode.value === '复式') {
    if (curType.value === 'SSQ') return `C(${fc},6) × ${bc} = ${result.value.bets} 注`
    if (curType.value === 'DLT') return `C(${fc},5) × C(${bc},2) = ${result.value.bets} 注`
    return posSel.value.map(p => p.length || 1).join(' × ') + ` = ${result.value.bets} 注`
  }
  if (curMode.value === '胆拖') {
    const backPart = backDantuo.value && bdc > 0
      ? `C(${btc},${2 - bdc})` : curType.value === 'DLT' ? `C(${bc},2)` : `${bc || 1}`
    return `C(${tc},${pick - dc}) × ${backPart} = ${result.value.bets} 注`
  }
  if (curMode.value === '组选6') return `C(${fc},3) = ${result.value.bets} 注`
  if (curMode.value === '组选3') return fc >= 2 ? '1 注（选 2 个号码，其中 1 个重复）' : '需选择 2 个号码'
  return ''
})
</script>

<template>
  <CommonDialog v-model="visible" title="💰 金额计算" width="720px" align-center class="calc-dialog">
    <!-- 彩种切换 -->
    <el-tabs v-model="curType" class="calc-type-tabs">
      <el-tab-pane v-for="t in LOTTERY_TABS" :key="t.code" :label="t.name" :name="t.code" />
    </el-tabs>

    <!-- 玩法选择 -->
    <div class="calc-mode-bar">
      <el-radio-group v-model="curMode" size="small">
        <el-radio-button v-for="m in curModes" :key="m.value" :value="m.value">{{ m.label }}</el-radio-button>
      </el-radio-group>
    </div>

    <!-- 号码选择区 -->
    <div class="calc-pick-area">
      <!-- 池选型 单式/复式 -->
      <template v-if="!positional && curMode !== '胆拖'">
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label front-label">{{ typeName }}前区</span>
            <span class="zone-count">已选 <em :class="{ ok: true }">{{ frontSel.length }}</em><template v-if="curMode === '单式'"> / {{ curType === 'DLT' ? 5 : 6 }}</template></span>
            <span v-if="curMode === '复式'" class="zone-hint">（多选即为复式）</span>
          </div>
          <div class="nb-grid">
            <button v-for="n in frontPool" :key="'f'+n"
              class="num-ball nb-front" :class="{ selected: frontSel.includes(n) }"
              @click="frontSel = toggleIn(frontSel, n, curMode === '单式' ? (curType === 'DLT' ? 5 : 6) : undefined)">{{ fmtNumber(false, n) }}</button>
          </div>
        </div>
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label back-label">后区</span>
            <span class="zone-count">已选 <em>{{ backSel.length }}</em><template v-if="curMode === '单式'"> / {{ curType === 'DLT' ? 2 : 1 }}</template></span>
          </div>
          <div class="nb-grid">
            <button v-for="n in backPool" :key="'b'+n"
              class="num-ball nb-back" :class="{ selected: backSel.includes(n) }"
              @click="backSel = toggleIn(backSel, n, curMode === '单式' ? (curType === 'DLT' ? 2 : 1) : undefined)">{{ fmtNumber(false, n) }}</button>
          </div>
        </div>
      </template>

      <!-- 池选型 胆拖 -->
      <template v-if="!positional && curMode === '胆拖'">
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label nb-dan-label">胆码</span>
            <span class="zone-count">已选 <em>{{ danSel.length }}</em> / 最多 {{ danMax }}</span>
          </div>
          <div class="nb-grid">
            <button v-for="n in frontPool" :key="'d'+n"
              class="num-ball nb-dan" :class="{ selected: danSel.includes(n) }"
              @click="toggleDan(n)">{{ fmtNumber(false, n) }}</button>
          </div>
        </div>
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label front-label">拖码</span>
            <span class="zone-count">已选 <em>{{ trailSel.length }}</em></span>
          </div>
          <div class="nb-grid">
            <button v-for="n in frontPool" :key="'t'+n"
              class="num-ball nb-front" :class="{ selected: trailSel.includes(n) }"
              @click="toggleTrail(n)">{{ fmtNumber(false, n) }}</button>
          </div>
        </div>
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label back-label">后区</span>
            <span class="zone-count">已选 <em>{{ backSel.length }}</em></span>
            <el-checkbox v-if="curType === 'DLT'" v-model="backDantuo" size="small" style="margin-left: 8px;">启用胆拖</el-checkbox>
          </div>
          <template v-if="!backDantuo || curType !== 'DLT'">
            <div class="nb-grid">
              <button v-for="n in backPool" :key="'db'+n"
                class="num-ball nb-back" :class="{ selected: backSel.includes(n) }"
                @click="backSel = toggleIn(backSel, n)">{{ fmtNumber(false, n) }}</button>
            </div>
          </template>
          <template v-else>
            <div style="margin-bottom: 6px; font-size: 12px; color: #f59e0b; font-weight: 600;">后区胆码（已选 {{ backDanSel.length }} / 最多 1）</div>
            <div class="nb-grid" style="margin-bottom: 10px;">
              <button v-for="n in backPool" :key="'bdb'+n"
                class="num-ball nb-dan" :class="{ selected: backDanSel.includes(n) }"
                @click="toggleBackDan(n)">{{ fmtNumber(false, n) }}</button>
            </div>
            <div style="margin-bottom: 6px; font-size: 12px; color: #2563eb; font-weight: 600;">后区拖码（已选 {{ backTrailSel.length }}）</div>
            <div class="nb-grid">
              <button v-for="n in backPool" :key="'bdt'+n"
                class="num-ball nb-back" :class="{ selected: backTrailSel.includes(n) }"
                @click="toggleBackTrail(n)">{{ fmtNumber(false, n) }}</button>
            </div>
          </template>
        </div>
      </template>

      <!-- 位置型 单式/复式 -->
      <template v-if="positional">
        <div v-for="pos in posCount" :key="pos" class="calc-zone calc-pos-zone">
          <div class="zone-title">
            <span class="zone-label front-label">{{ posLabels[pos - 1] }}位</span>
            <span class="zone-count">已选 <em>{{ posSel[pos - 1]?.length ?? 0 }}</em><template v-if="curMode === '单式'"> / 1</template></span>
          </div>
          <div class="nb-grid">
            <button v-for="n in 10" :key="'p'+pos+n"
              class="num-ball nb-front" :class="{ selected: (posSel[pos - 1] ?? []).includes(n - 1) }"
              @click="togglePos(pos - 1, n - 1)">{{ n - 1 }}</button>
          </div>
        </div>
      </template>

      <!-- FC3D 组选 -->
      <template v-if="curType === 'FC3D' && (curMode === '组选6' || curMode === '组选3')">
        <div class="calc-zone">
          <div class="zone-title">
            <span class="zone-label front-label">选号</span>
            <span class="zone-count">已选 <em>{{ frontSel.length }}</em></span>
            <span class="zone-hint">（{{ curMode === '组选6' ? '选 3+ 个不同号码' : '选 2 个号码' }}）</span>
          </div>
          <div class="nb-grid">
            <button v-for="n in 10" :key="'g'+n"
              class="num-ball nb-front" :class="{ selected: frontSel.includes(n - 1) }"
              @click="frontSel = toggleIn(frontSel, n - 1, curMode === '组选3' ? 2 : undefined)">{{ n - 1 }}</button>
          </div>
        </div>
      </template>
    </div>

    <!-- 操作栏 -->
    <div class="calc-action">
      <el-button size="small" @click="clearAll">清空选号</el-button>
    </div>

    <!-- 计算结果 -->
    <div class="calc-result">
      <div class="calc-formula" v-if="formulaText">
        <span class="calc-formula-label">公式</span>
        <code>{{ formulaText }}</code>
      </div>
      <div class="calc-stats">
        <div class="calc-stat">
          <div class="calc-stat-label">注数</div>
          <div class="calc-stat-value">{{ result.bets }} 注</div>
        </div>
        <div class="calc-stat">
          <div class="calc-stat-label">单价</div>
          <div class="calc-stat-value sm">¥2 / 注</div>
        </div>
        <div class="calc-stat main">
          <div class="calc-stat-label">合计金额</div>
          <div class="calc-stat-value cost">¥{{ result.cost }}</div>
        </div>
      </div>
    </div>
  </CommonDialog>
</template>

<style scoped>
.calc-type-tabs { margin-bottom: 8px; }
.calc-type-tabs :deep(.el-tabs__header) { margin-bottom: 0; }

.calc-mode-bar { margin-bottom: 16px; }

.calc-pick-area { display: flex; flex-direction: column; gap: 14px; margin-bottom: 12px; }

.calc-zone {}
.zone-title { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
.zone-label {
  font-size: 12px; font-weight: 600; padding: 2px 10px;
  border-radius: 4px; color: #fff;
}
.front-label { background: #e6393a; }
.back-label { background: #2563eb; }
.nb-dan-label { background: #f59e0b; }
.zone-count { font-size: 13px; color: #606266; }
.zone-count em { font-style: normal; font-weight: 600; color: #e6393a; }
.zone-count em.ok { color: #67c23a; }
.zone-hint { font-size: 12px; color: #909399; }

.nb-grid { display: flex; flex-wrap: wrap; gap: 6px; }

.num-ball {
  width: 34px; height: 34px; border-radius: 50%;
  border: 2px solid #dcdfe6; background: #fff;
  font-size: 13px; font-weight: 600; color: #606266;
  cursor: pointer; transition: all 0.15s;
  display: flex; align-items: center; justify-content: center;
}
.num-ball:hover { border-color: #c0c4cc; transform: scale(1.08); }

.nb-front.selected {
  background: #e6393a; border-color: #e6393a; color: #fff;
  box-shadow: 0 2px 6px rgba(230, 57, 58, 0.3);
}
.nb-back.selected {
  background: #2563eb; border-color: #2563eb; color: #fff;
  box-shadow: 0 2px 6px rgba(37, 99, 235, 0.3);
}
.nb-dan.selected {
  background: #f59e0b; border-color: #f59e0b; color: #fff;
  box-shadow: 0 2px 6px rgba(245, 158, 11, 0.3);
}

.calc-pos-zone {
  padding-bottom: 8px;
  border-bottom: 1px solid #f3f4f6;
}
.calc-pos-zone:last-child { border-bottom: none; }

.calc-action { display: flex; justify-content: flex-end; margin-bottom: 12px; }

.calc-result {
  background: linear-gradient(135deg, #f0f7ff 0%, #faf5ff 100%);
  border: 1px solid #e5e7eb; border-radius: 10px;
  padding: 16px 20px;
}
.calc-formula { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
.calc-formula-label { font-size: 12px; color: #909399; flex-shrink: 0; }
.calc-formula code {
  font-size: 13px; color: #3b82f6; background: #eff6ff;
  padding: 2px 8px; border-radius: 4px;
}
.calc-stats { display: flex; align-items: flex-end; gap: 24px; }
.calc-stat {}
.calc-stat.main { margin-left: auto; text-align: right; }
.calc-stat-label { font-size: 12px; color: #6b7280; margin-bottom: 2px; }
.calc-stat-value { font-size: 20px; font-weight: 700; color: #1f2937; }
.calc-stat-value.sm { font-size: 15px; color: #6b7280; }
.calc-stat-value.cost { font-size: 28px; color: #e6393a; font-weight: 800; }
</style>
