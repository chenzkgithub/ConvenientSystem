<script setup lang="ts">
/**
 * 历史号码匹配 弹窗（走势图专用）
 * 点选号码球后确定，由父组件发起全库检索：号码不必手输，故无需解析与合法性校验
 * 用法：const ref = ref(); ref.value?.open(当前已生效的匹配号码)
 */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { fullscreenElement } from '@/common/utils/fullscreen'
import { fmtNumber, type LotteryZone, type LotteryMatchSpec } from '@/common/lottery'
import CommonDialog from '@/common/components/CommonDialog.vue'

const props = defineProps<{
  /** 彩种选号分区（号码池来源） */
  pickZones: LotteryZone[]
}>()

const emit = defineEmits<{
  /** 确定匹配：号码集合条件与数位条件（按彩种取其一，另一类为空） */
  confirm: [spec: LotteryMatchSpec]
}>()

const visible = ref(false)

/**
 * 数位分区（排列五/福彩3D 的万位…个位），按数位序升序。
 * 这类彩种按位开奖、同一数字可在多个数位上重复开出（如 55555），
 * 故条件不能用「一个号码集合」表达，须逐位各给候选数字。
 */
const posZones = computed(() => props.pickZones
  .filter(z => z.positional)
  .slice()
  .sort((a, b) => a.posIndex - b.posIndex))

/** 位置型彩种走逐位点选，其余走前区/后区号码集合点选 */
const isPositional = computed(() => posZones.value.length > 0)

/** 口径说明：压成单行，避免把弹窗顶高而出现内部滚动条（数位彩种有 5 行号码球，竖向很吃紧） */
const hintText = computed(() => (isPositional.value
  ? '每位最多选 1 个，不选表示该位不限；只列出指定位置全部对上的期，按期号从新到旧展示'
  : '只列出同时包含所选号码的期（选几个就要几个全开出），按期号从新到旧展示'))

// 号码集合条件（大乐透/双色球）
const front = ref<number[]>([])
const back = ref<number[]>([])
// 数位条件（排列五/福彩3D）：下标即数位序，每位一组候选数字，空数组表示该位不限
const pos = ref<number[][]>([])

/** 某区号码池（号码集合条件用，数位分区不参与合并） */
function pool(source: 'front' | 'back'): number[] {
  const set = new Set<number>()
  for (const z of props.pickZones) {
    if (z.source === source && !z.positional) for (const n of z.numbers) set.add(n)
  }
  return [...set].sort((a, b) => a - b)
}

const frontPool = computed(() => pool('front'))
const backPool = computed(() => pool('back'))

/**
 * 某区可选上限：取本彩种该区各非数位选号分区的选号个数之和（大乐透 5+2、双色球 6+1）
 * ——即单期开出的号码个数；条件是“全部包含”，选超过这个数则不可能有任何期命中。
 * 数位条件同口径逐位限制：该位单期只开出 1 个数字，故每位最多选 1 个（见 posMax）。
 */
function maxOf(source: 'front' | 'back'): number {
  const sum = props.pickZones
    .filter(z => z.source === source && !z.positional)
    .reduce((s, z) => s + z.pick, 0)
  // 配置未给 pick 时不做限制，避免选不了号码
  return sum > 0 ? sum : pool(source).length
}

const maxFront = computed(() => maxOf('front'))
const maxBack = computed(() => maxOf('back'))

/** 每个数位可选个数：取该分区的选号个数（固定为 1），配置缺失时兼容为 1 */
function posMax(i: number): number {
  return posZones.value[i]?.pick || 1
}

/** 数位条件已选总个数与上限（各位上限之和 = 单期开出个数：排列五 5、福彩3D 3） */
const posCount = computed(() => pos.value.reduce((s, d) => s + d.length, 0))
const posMaxTotal = computed(() => posZones.value.reduce((s, _z, i) => s + posMax(i), 0))

/** 单数字号码池不补零，两位号码池补零两位 */
function fmt(numbers: number[], n: number): string {
  return fmtNumber(numbers.every(x => x < 10), n)
}

/** 某数位上增删候选数字：各位互不影响，故同一数字可在多位上同时选中 */
function togglePos(i: number, n: number) {
  const list = pos.value[i]
  if (!list) return
  const at = list.indexOf(n)
  if (at >= 0) {
    list.splice(at, 1)
    return
  }
  const max = posMax(i)
  if (list.length >= max) {
    ElMessage.warning({
      message: `${posZones.value[i]?.label ?? `第${i + 1}位`}最多选 ${max} 个（该位单期只开出 ${max} 个数字）`,
      appendTo: fullscreenElement(),
    })
    return
  }
  list.push(n)
}

/** 该数字是否因本位已选满而不可再选（已选中的仍可点击取消换号） */
function isPosFull(i: number, n: number): boolean {
  const list = pos.value[i]
  return !!list && list.length >= posMax(i) && !list.includes(n)
}

function toggle(source: 'front' | 'back', n: number) {
  const list = source === 'front' ? front : back
  const i = list.value.indexOf(n)
  if (i >= 0) {
    list.value.splice(i, 1)
    return
  }
  const max = source === 'front' ? maxFront.value : maxBack.value
  if (list.value.length >= max) {
    const label = source === 'front' ? '前区号码' : '后区号码'
    ElMessage.warning({
      message: `${label}最多选 ${max} 个（单期只开出 ${max} 个）`,
      appendTo: fullscreenElement(),
    })
    return
  }
  list.value.push(n)
}

/** 该号码是否因本区已选满而不可再选（已选中的仍可点击取消） */
function isFull(source: 'front' | 'back', n: number): boolean {
  const list = source === 'front' ? front : back
  const max = source === 'front' ? maxFront.value : maxBack.value
  return list.value.length >= max && !list.value.includes(n)
}

function clear() {
  front.value = []
  back.value = []
  pos.value = posZones.value.map(() => [])
}

function confirm() {
  // 展示与请求都按升序，避免点选顺序影响回显
  const asc = (list: number[]) => [...list].sort((a, b) => a - b)
  const spec: LotteryMatchSpec = isPositional.value
    ? { front: [], back: [], pos: pos.value.map(asc) }
    : { front: asc(front.value), back: asc(back.value), pos: [] }
  if (spec.front.length === 0 && spec.back.length === 0 && spec.pos.every(d => d.length === 0)) {
    ElMessage.warning({ message: '请先选择要匹配的号码', appendTo: fullscreenElement() })
    return
  }
  emit('confirm', spec)
  visible.value = false
}

/** 打开弹窗：带入已生效的匹配条件，便于在原有条件上增删 */
function open(current?: LotteryMatchSpec | null) {
  front.value = [...(current?.front ?? [])]
  back.value = [...(current?.back ?? [])]
  // 按当前彩种数位数补齐，避免切换彩种后条件下标错位
  pos.value = posZones.value.map((_, i) => [...(current?.pos?.[i] ?? [])])
  visible.value = true
}

defineExpose({ open })
</script>

<template>
  <CommonDialog
    v-model="visible"
    title="历史号码匹配"
    width="560px"
    destroy-on-close
  >
    <!-- 单行口径说明（匹配时统计期数与开奖日期不参与筛选，已在工具栏控件禁用上体现，此处不重复） -->
    <div class="match-hint">{{ hintText }}</div>

    <!-- 位置型彩种（排列五/福彩3D）：逐位给数字，同一数字可在多位上重复选中 -->
    <template v-if="isPositional">
      <div class="match-zone-head">
        <span class="match-zone-label">开奖数字</span>
        <span class="match-zone-count">已选 {{ posCount }} / {{ posMaxTotal }} 个</span>
      </div>
      <div v-for="(z, i) in posZones" :key="z.key" class="pos-row">
        <span class="pos-label">{{ z.label }}</span>
        <div class="ball-grid">
          <span v-for="n in z.numbers" :key="'p' + i + '-' + n" class="num-ball front-ball"
            :class="{ selected: pos[i]?.includes(n), full: isPosFull(i, n) }" @click="togglePos(i, n)">
            {{ n }}
          </span>
        </div>
      </div>
    </template>

    <template v-else>
      <div class="match-zone">
        <div class="match-zone-head">
          <span class="match-zone-label">前区号码</span>
          <span class="match-zone-count">已选 {{ front.length }} / {{ maxFront }} 个</span>
        </div>
        <div class="ball-grid">
          <span v-for="n in frontPool" :key="'f' + n" class="num-ball front-ball"
            :class="{ selected: front.includes(n), full: isFull('front', n) }" @click="toggle('front', n)">
            {{ fmt(frontPool, n) }}
          </span>
        </div>
      </div>

      <div class="match-zone" v-if="backPool.length > 0">
        <div class="match-zone-head">
          <span class="match-zone-label">后区号码</span>
          <span class="match-zone-count">已选 {{ back.length }} / {{ maxBack }} 个</span>
        </div>
        <div class="ball-grid">
          <span v-for="n in backPool" :key="'b' + n" class="num-ball back-ball"
            :class="{ selected: back.includes(n), full: isFull('back', n) }" @click="toggle('back', n)">
            {{ fmt(backPool, n) }}
          </span>
        </div>
      </div>
    </template>

    <template #footer>
      <el-button @click="clear">清空</el-button>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" @click="confirm">开始匹配</el-button>
    </template>
  </CommonDialog>
</template>

<style scoped>
.match-hint {
  margin-bottom: 10px;
  font-size: 12px;
  color: #909399;
}

/* 数位行：位名与 0-9 同行排列，多至五位仍不需弹窗内滚动 */
.pos-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.pos-row + .pos-row {
  margin-top: 6px;
}

.pos-label {
  width: 36px;
  flex-shrink: 0;
  font-size: 12px;
  font-weight: 600;
  color: #303133;
}

.match-zone + .match-zone {
  margin-top: 12px;
}

.match-zone-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.match-zone-label {
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}

.match-zone-count {
  font-size: 12px;
  color: #909399;
}

.ball-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

/* 号码球样式与选号面板保持一致，仅尺寸缩小：既要容下 33 个号码，也要让五行数位不把弹窗顶出滚动条 */
.num-ball {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  border: 2px solid #dcdfe6;
  background: #fff;
  font-size: 12px;
  font-weight: 600;
  color: #606266;
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  align-items: center;
  justify-content: center;
  user-select: none;
}

.num-ball:hover {
  border-color: #c0c4cc;
  transform: scale(1.08);
}

/* 本区已选满：未选中的号码置灰，点下只给提示而不选中（不用 pointer-events 屏蔽，否则用户无反馈） */
.num-ball.full {
  border-color: #ebeef5;
  color: #c0c4cc;
  cursor: not-allowed;
}

.num-ball.full:hover {
  border-color: #ebeef5;
  transform: none;
}

.front-ball.selected {
  background: #e6393a;
  border-color: #e6393a;
  color: #fff;
  box-shadow: 0 2px 8px rgba(230, 57, 58, 0.35);
}

.back-ball.selected {
  background: #2563eb;
  border-color: #2563eb;
  color: #fff;
  box-shadow: 0 2px 8px rgba(37, 99, 235, 0.35);
}
</style>
