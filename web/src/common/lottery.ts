import { httpGet, httpPost } from '@/api/request'

/** 彩种代码：大乐透/双色球/排列五/福彩3D */
export type LotteryType = 'DLT' | 'SSQ' | 'PL5' | 'FC3D'

/** 号码统计（单个号码） */
export interface NumberStat {
  number: number
  count: number
  currentMiss: number
  /** 展示窗口首期之前的历史遗漏（后端按库内早期历史计算，走势图遗漏种子） */
  initialMiss: number
  avgMiss: number
  maxMiss: number
  maxConsecutive: number
}

/**
 * 彩种分区：与后端 LotteryZoneDto 对应。
 * - 池选型分区（DLT/SSQ）：从 numbers 号码池中选 pick 个不重复号码；
 * - 位置型分区（PL5/FC3D）：positional=true，对应开奖号码 front[posIndex]，各选 1 个。
 */
export interface LotteryZone {
  key: string
  label: string
  numbers: number[]
  source: 'front' | 'back'
  positional: boolean
  posIndex: number
  pick: number
  pickZoneKey: string
  stats?: NumberStat[]
}

/** 彩种配置（选号页渲染用） */
export interface LotteryConfig {
  code: LotteryType
  name: string
  pickZones: LotteryZone[]
}

/** 开奖记录 */
export interface LotteryDraw {
  id: number
  issueNumber: string
  drawDate: string
  front: number[]
  back: number[]
}

/** 走势图数据 */
export interface TrendData {
  totalPeriods: number
  draws: LotteryDraw[]
  groups: LotteryZone[]
  /** 是否历史号码匹配模式（draws 只保留命中全部条件的期，期与期不相邻，遗漏/连线/纵向连号不成立） */
  matchMode: boolean
  /** 匹配到的总期数（大于 draws.length 时说明已被展示上限截断） */
  matchTotal: number
}

/** 首页彩票单注中奖明细 */
export interface LotteryHomeBetResult {
  pick: string
  prize: string
  isWin: boolean
}

/** 首页彩票中奖结果（单彩种：最新一期开奖 + 本人逐注中奖结果） */
export interface LotteryHomeResult {
  type: string
  name: string
  issueNumber: string
  drawDate: string | null
  positional: boolean
  front: number[]
  back: number[]
  betCount: number
  winCount: number
  bets: LotteryHomeBetResult[]
}

/** 选号记录（分页列表行） */
export interface LotteryRecordItem {
  id: number
  front: number[]
  back: number[]
  /** 所属期号（保存时默认取下一期；历史记录为空） */
  issueNumber: string | null
  /** 开奖日期（保存时默认取下一期开奖日；历史记录为空） */
  drawDate: string | null
  createdAt: string
}

/** 官方中奖明细单行（官网通告口径） */
export interface LotteryPrizeGrade {
  grade: string
  count: number | null
  money: number | null
}

/** 指定开奖期的官网通告数据（走势图双击查看用） */
export interface LotteryDrawNotice {
  issueNumber: string
  drawDate: string
  front: number[]
  back: number[]
  grades: LotteryPrizeGrade[]
  salesAmount: number | null
  poolBalance: number | null
  /** 一等奖中奖地区文本（福彩双色球官网通告口径；无则 null） */
  prizeArea: string | null
  /** 官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 null） */
  noticeUrl: string | null
}

/** 逐注命中明细（验奖弹窗高亮命中号码用） */
export interface LotteryHitResult {
  prize: string
  isWin: boolean
  /** 命中的前区/红球号码（池选型） */
  frontHits: number[]
  /** 命中的后区/蓝球号码（池选型） */
  backHits: number[]
  /** 位置型按位是否命中（与本注号码同下标） */
  positionHits: boolean[]
  frontHitCount: number
  backHitCount: number
  /** 命中情况文字说明 */
  hitSummary: string
}

/** 选号验证结果：本注奖级 + 命中明细 + 对应开奖期的官网通告数据 */
export interface LotteryVerifyResult {
  recordId: number
  pick: string
  hasDraw: boolean
  issueNumber: string
  drawDate: string | null
  drawFront: number[]
  drawBack: number[]
  prize: string
  isWin: boolean
  /** 本注奖金（税前） */
  money: number | null
  /** 本注应缴个税（单注不超 1 万为 0） */
  tax: number | null
  /** 本注税后实得奖金 */
  moneyAfterTax: number | null
  /** 本注所中奖级的全国中奖注数 */
  gradeCount: number | null
  /** 本注对应的官方奖级名（用于高亮全国中奖明细中本注那一行） */
  matchedGrade: string | null
  /** 本注命中明细（未开奖时为 null） */
  hit: LotteryHitResult | null
  grades: LotteryPrizeGrade[]
  salesAmount: number | null
  poolBalance: number | null
  /** 一等奖中奖地区文本（福彩双色球官网通告口径；无则 null） */
  prizeArea: string | null
  /** 官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 null） */
  noticeUrl: string | null
}

/** 整期批量验奖中的单注结果（开奖号码与通告在外层，不逐注重复） */
export interface LotteryIssueBet {
  recordId: number
  front: number[]
  back: number[]
  pick: string
  createdAt: string
  prize: string
  isWin: boolean
  hit: LotteryHitResult | null
  money: number | null
  tax: number | null
  moneyAfterTax: number | null
  gradeCount: number | null
  matchedGrade: string | null
}

/** 整期批量验奖结果：一份开奖号码与官网通告 + 逐注结果 + 合计奖金 */
export interface LotteryIssueVerifyResult {
  hasDraw: boolean
  issueNumber: string
  drawDate: string | null
  drawFront: number[]
  drawBack: number[]
  bets: LotteryIssueBet[]
  betCount: number
  winCount: number
  /** 合计奖金（税前） */
  totalMoney: number
  totalTax: number
  totalMoneyAfterTax: number
  /** 是否至少有一注取到官方奖金（false 时合计金额不展示） */
  moneyKnown: boolean
  grades: LotteryPrizeGrade[]
  salesAmount: number | null
  poolBalance: number | null
  prizeArea: string | null
  noticeUrl: string | null
}

/** 彩种页签配置（选号记录页四个页签） */
export const LOTTERY_TABS: { code: LotteryType; name: string }[] = [
  { code: 'SSQ', name: '双色球' },
  { code: 'DLT', name: '大乐透' },
  { code: 'PL5', name: '排列五' },
  { code: 'FC3D', name: '福彩3D' },
]

/** 彩种是否位置型（号码按位存储、允许 0、显示不补零） */
export function isPositional(type: string): boolean {
  return type === 'PL5' || type === 'FC3D'
}

/** 号码显示：位置型单数字，池选型补零两位 */
export function fmtNumber(positional: boolean, n: number): string {
  return positional ? String(n) : String(n).padStart(2, '0')
}

/** 获取彩种配置（名称与选号分区） */
export function getLotteryConfig(type: string) {
  return httpGet<LotteryConfig>('/api/Common/LotteryDraw/Config', { type })
}

/**
 * 历史号码匹配条件：普通彩种（大乐透/双色球）按号码集合，
 * 位置型彩种（排列五/福彩3D）按数位：各位独立，故多个位可要求同一个数字。
 */
export interface LotteryMatchSpec {
  /** 前区号码（集合口径：该期必须包含这里的每一个号码） */
  front: number[]
  /** 后区号码 */
  back: number[]
  /** 各数位候选数字，下标即数位序（万→个），空数组表示该位不限 */
  pos: number[][]
}

/**
 * 获取走势图数据：指定日期区间时按开奖日期筛选，否则取最近 periods 期。
 * 传入 match（条件非空）时转为历史号码匹配模式：忽略期数与日期区间，
 * 在全库内检索同时满足全部条件的期，按期号降序返回。
 */
export function getLotteryTrend(type: string, periods: number, startDate?: string, endDate?: string,
  match?: LotteryMatchSpec | null) {
  const params: Record<string, unknown> = { type, periods }
  if (startDate) params.startDate = startDate
  if (endDate) params.endDate = endDate
  if (match?.front.length) params.matchFront = match.front.join(',')
  if (match?.back.length) params.matchBack = match.back.join(',')
  // 数位条件序列化为 “数位序:该位候选数字” 串（各位均为单数字 0-9，故数字直接连写）
  const pos = (match?.pos ?? [])
    .map((digits, i) => (digits.length > 0 ? `${i}:${digits.join('')}` : ''))
    .filter(s => s)
    .join(',')
  if (pos) params.matchPos = pos
  return httpGet<TrendData>('/api/Common/LotteryDraw/Trend', params)
}

/** 获取各彩种最新开奖与当前用户中奖结果（首页展示用） */
export function getLotteryHomeResults() {
  return httpGet<LotteryHomeResult[]>('/api/Common/Lottery/HomeResults')
}

/** 获取指定开奖期的官网通告数据（全国中奖明细/销量/奖池） */
export function getLotteryDrawNotice(type: string, issue: string) {
  return httpGet<LotteryDrawNotice>('/api/Common/LotteryDraw/Notice', { type, issue })
}

/** 验证指定选号记录的中奖结果（含官网通告中奖明细） */
export function verifyLotteryBet(id: number) {
  return httpGet<LotteryVerifyResult>('/api/Common/LotteryRecord/Verify', { id })
}

/**
 * 批量验证整期选号：date（yyyy-MM-dd）为开奖日，缺省时后端取最新一期
 */
export function verifyLotteryIssue(type: LotteryType, date?: string) {
  const params: Record<string, unknown> = { type }
  if (date) params.date = date
  return httpGet<LotteryIssueVerifyResult>('/api/Common/LotteryRecord/VerifyIssue', params)
}

// ─────────────── 玩法规则（自官网条文每日抓取，判奖与奖级对照表的数据源）───────────────

/** 一个中奖条件：前区至少命中 front 个 且 后区至少命中 back 个 */
export interface LotteryHitCond {
  front: number
  back: number
}

/** 单个奖级的判奖规则 */
export interface LotteryGradeRule {
  /** 官方奖级名（一等奖/福运奖/组选3…） */
  grade: string
  /** 系统内部奖级名（判奖结果口径） */
  systemGrade: string
  /** 判定顺序（升序命中即止，官方“不兼中兼得”） */
  order: number
  /** 判定方式：hit=按命中个数 exact=按位全同 set3/set6=组选 */
  match: 'hit' | 'exact' | 'set3' | 'set6'
  /** 命中条件（多个为“或”关系；位置型为空） */
  conds: LotteryHitCond[]
  /** 单注固定奖金；浮动奖级或分档奖金为 null */
  fixedMoney: number | null
  /** 奖金条文原文 */
  moneyText: string | null
  /** 中奖条件条文原文 */
  conditionText: string | null
  /** 附条件奖级（如双色球福运奖仅在执行特别规定期间设立） */
  conditional: boolean
}

/** 规则版本 */
export interface LotteryRuleVersion {
  id: number
  version: number
  status: number
  statusText: string
  sourceUrl: string | null
  ruleText: string | null
  grades: LotteryGradeRule[]
  crawledAt: string
  effectiveAt: string | null
  reviewedBy: string | null
  remark: string | null
}

/** 玩法规则弹窗数据 */
export interface LotteryRuleView {
  lotteryType: string
  typeName: string
  positional: boolean
  /** 对照表每行画的前区圆点数 */
  frontTotal: number
  /** 对照表每行画的后区圆点数（位置型为 0） */
  backTotal: number
  frontLabel: string
  backLabel: string
  /** 当前判奖依据的版本（version=0 表示内置兜底规则） */
  current: LotteryRuleVersion | null
  usingDefault: boolean
  /** 待审版本（官网条文有变动时给出） */
  pending: LotteryRuleVersion | null
}

/** 获取彩种玩法规则（当前生效版本 + 待审版本） */
export function getLotteryRuleView(type: string) {
  return httpGet<LotteryRuleView>('/api/Common/LotteryRule/View', { type })
}

/** 规则版本历史 */
export function getLotteryRuleVersions(type: string) {
  return httpGet<LotteryRuleVersion[]>('/api/Common/LotteryRule/Versions', { type })
}

/** 审核待审版本：approve=true 启用，false 驳回 */
export function reviewLotteryRule(id: number, approve: boolean, remark?: string) {
  return httpPost<boolean>('/api/Common/LotteryRule/Review', { id, approve, remark })
}

/** 立即抓取官网规则条文（后台执行） */
export function crawlLotteryRule(type: string) {
  return httpPost<string>(`/api/Common/LotteryRule/Crawl?type=${encodeURIComponent(type)}`, {})
}

// ─────────────── 智能分析（多维度评分与号码推荐）───────────────

/** 单注号码（保存选号与 AI 组合共用） */
export interface LotteryBetItem {
  front: number[]
  back: number[]
}

/** 单个号码的多维度评分 */
export interface NumberScore {
  number: number
  score: number
  hotScore: number
  coldScore: number
  missScore: number
  consecutiveScore: number
  zoneScore: number
  currentMiss: number
  avgMiss: number
  maxMiss: number
  count: number
  zoneLabel: string
}

/** 智能分析结果 */
export interface LotteryAnalysis {
  type: string
  typeName: string
  periods: number
  nextIssue: string | null
  nextDrawDate: string | null
  frontScores: NumberScore[]
  backScores: NumberScore[]
  recommendedFront: number[]
  recommendedBack: number[]
  hotNumbers: number[]
  coldNumbers: number[]
  generatedBets: LotteryBetItem[]
  summary: string
}

/** 获取智能分析报告 */
export function getLotteryAnalysis(type: string, periods = 100) {
  return httpGet<LotteryAnalysis>('/api/Common/LotteryAnalysis/Predict', { type, periods })
}

