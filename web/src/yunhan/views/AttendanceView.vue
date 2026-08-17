<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getAttendance, getAttendanceDtl, getDailyRanking, getDeptTree } from '@/yunhan/api/attendance'
import type { AttendanceRow, DeptNode, DeptViewRaw, RequestDto } from '@/yunhan/types'
import CommonDialog from '@/common/components/CommonDialog.vue'

const DEFAULT_DEPT_NAME = '开发二组'
const SUM_KEYS: (keyof AttendanceRow)[] = [
  'workDuration',
  'leaveDuration',
  'travelDuration',
  'overtimeDuration',
  'actualDuration',
]

// ========== 筛选条件 ==========
const month = ref('')
const nameFilter = ref('')

// ========== 组织架构树 ==========
const treeData = ref<DeptNode[]>([])
const activeFullCode = ref('')
const currentNodeId = ref<number>()
const expandedKeys = ref<number[]>([])
const treeProps = { label: 'name', children: 'children' }

// ========== 汇总表 ==========
const detailList = ref<AttendanceRow[]>([]) // 当前部门原始明细
const sumList = ref<AttendanceRow[]>([]) // 按人分组汇总

// ========== 明细弹层 ==========
const detailVisible = ref(false)
const detailData = ref<AttendanceRow[]>([])
const detailName = ref('')
const detailLoading = ref(false)

// ========== 排行弹层 ==========
const rankingVisible = ref(false)
const originRankingList = ref<AttendanceRow[]>([])
const rankingKeyword = ref('')
const rankingTitle = ref('')
const rankingLoading = ref(false)
let rankingDebounceTimer: number | undefined

// 排行搜索防抖：输入后 220ms 才真正过滤，减少大列表频繁重算
const debouncedKeyword = ref('')
watch(rankingKeyword, (kw) => {
  if (rankingDebounceTimer) window.clearTimeout(rankingDebounceTimer)
  rankingDebounceTimer = window.setTimeout(() => {
    debouncedKeyword.value = kw
  }, 220)
})

const rankingList = computed(() => {
  const kw = debouncedKeyword.value.trim().toLowerCase()
  if (!kw) return originRankingList.value
  return originRankingList.value.filter((item) => {
    const name = (item.userName || '').toLowerCase()
    const dept = (item.deptName || '').toLowerCase()
    const fullDept = (item.fullDeptName || '').toLowerCase()
    return name.includes(kw) || dept.includes(kw) || fullDept.includes(kw)
  })
})

// ========== 工具 ==========
function statusText(s?: number) {
  return s === 1 ? '离职' : s === 2 ? '试用' : s === 3 ? '在职' : '未知'
}

function setDefaultMonth() {
  const now = new Date()
  const y = now.getFullYear()
  const m = String(now.getMonth() + 1).padStart(2, '0')
  month.value = `${y}-${m}`
}

// 列表转树（parent_id === -99999 为根）
function listToTree(list: DeptViewRaw[]): DeptNode[] {
  const map = new Map<number, DeptNode>()
  const roots: DeptNode[] = []
  list.forEach((raw) => {
    const id = Number(raw.dept_id)
    map.set(id, {
      id,
      name: raw.dept_name,
      parentId: Number(raw.parent_id),
      full_code: raw.full_code ?? '',
      level: Number(raw.lvl || 0),
      children: [],
    })
  })
  map.forEach((node) => {
    if (node.parentId === -99999) {
      roots.push(node)
      return
    }
    const parent = map.get(node.parentId)
    if (parent) parent.children.push(node)
    else roots.push(node)
  })
  return roots
}

function findNodeByFullCode(tree: DeptNode[], fullCode: string): DeptNode | null {
  for (const node of tree) {
    if (node.full_code === fullCode) return node
    if (node.children.length) {
      const found = findNodeByFullCode(node.children, fullCode)
      if (found) return found
    }
  }
  return null
}

function findNodeByName(tree: DeptNode[], name: string): DeptNode | null {
  for (const node of tree) {
    if (node.name === name) return node
    if (node.children.length) {
      const found = findNodeByName(node.children, name)
      if (found) return found
    }
  }
  return null
}

// 收集目标节点的所有祖先 id（用于默认展开）
function collectParentIds(tree: DeptNode[], targetId: number): number[] {
  const ids: number[] = []
  const dfs = (node: DeptNode, path: number[]): boolean => {
    if (node.id === targetId) {
      ids.push(...path)
      return true
    }
    for (const child of node.children) {
      if (dfs(child, [...path, node.id])) return true
    }
    return false
  }
  tree.forEach((root) => dfs(root, []))
  return ids
}

// 按人分组求和（以钉钉用户ID为键，避免同名人员被合并；同时保留 ddUserId/deptId 供查看明细精确定位）
function groupSum(list: AttendanceRow[]): AttendanceRow[] {
  const map = new Map<string, AttendanceRow>()
  list.forEach((row) => {
    const k = row.ddUserId || row.userName
    if (!map.has(k)) {
      const base = {
        userName: row.userName,
        ddUserId: row.ddUserId,
        avatar: row.avatar || '',
        hiredDate: row.hiredDate,
        employeeStatus: row.employeeStatus,
        deptName: row.deptName,
        deptId: row.deptId,
      } as AttendanceRow
      SUM_KEYS.forEach((s) => ((base as any)[s] = 0))
      map.set(k, base)
    }
    const item = map.get(k)!
    SUM_KEYS.forEach((s) => ((item as any)[s] += Number((row as any)[s]) || 0))
  })
  return [...map.values()].map((item) => {
    SUM_KEYS.forEach((s) => ((item as any)[s] = Math.round((item as any)[s] * 100) / 100))
    return item
  })
}

// ========== 数据加载 ==========
async function loadDeptTree() {
  try {
    const json = await getDeptTree()
    treeData.value = Array.isArray(json) ? listToTree(json) : []
  } catch {
    /* 错误已由 request.ts 弹出提示 */
    treeData.value = []
  }
}

function buildParams(fullCode: string): RequestDto | null {
  if (!month.value) {
    ElMessage.warning('请选择月份')
    return null
  }
  return {
    fullCode,
    userName: nameFilter.value.trim(),
    month: month.value,
    orderby: '',
  }
}

async function fetchAllData(params: RequestDto) {
  detailList.value = []
  sumList.value = []
  try {
    const list = await getAttendance(params)
    detailList.value = list || []
    sumList.value = groupSum(detailList.value)
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function searchByDeptCode(fullCode: string) {
  activeFullCode.value = fullCode
  const p = buildParams(fullCode)
  if (!p) return
  await fetchAllData(p)
}

function searchByFilter() {
  // 根节点（公司）full_code 为空字符串，不能用真值判空判断是否已选；以是否选中节点为准
  if (currentNodeId.value == null) {
    ElMessage.warning('请左侧选择部门')
    return
  }
  searchByDeptCode(activeFullCode.value)
}

function onNodeClick(data: DeptNode) {
  currentNodeId.value = data.id
  searchByDeptCode(data.full_code)
}

// ========== 明细 ==========
async function openDetail(name: string, deptId = '', ddUserId = '') {
  detailName.value = name
  detailData.value = []
  detailVisible.value = true
  detailLoading.value = true
  try {
    // 传入 ddUserId 时按其精确过滤（避免同名混淆），无则回退到原来的部门+姓名过滤
    const req: RequestDto = {
      fullCode: ddUserId ? '' : (deptId !== '' ? deptId : activeFullCode.value),
      userName: ddUserId ? '' : name,
      ddUserId,
      month: month.value,
      orderby: '',
    }
    detailData.value = (await getAttendanceDtl(req)) || []
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    detailLoading.value = false
  }
}

// ========== 排行 ==========
async function openDailyRanking() {
  // 根节点（公司）full_code 为空字符串，改用是否选中节点判断，避免选中公司时误报“请选择部门”
  if (currentNodeId.value == null) {
    ElMessage.warning('请先在左侧选择部门')
    return
  }
  if (!month.value) {
    ElMessage.warning('请先选择月份')
    return
  }
  rankingVisible.value = true
  rankingKeyword.value = ''
  debouncedKeyword.value = ''
  rankingTitle.value = `义乌市昀晗贸易有限公司 ${month.value} 排行前100`
  rankingLoading.value = true
  try {
    const req: RequestDto = { fullCode: '', userName: '', month: month.value, orderby: '' }
    originRankingList.value = (await getDailyRanking(req)) || []
  } catch {
    /* 错误已由 request.ts 弹出提示 */
    originRankingList.value = []
  } finally {
    rankingLoading.value = false
  }
}

// ========== 初始化 ==========
onMounted(async () => {
  setDefaultMonth()
  await loadDeptTree()
  if (!treeData.value.length) return
  const target = findNodeByName(treeData.value, DEFAULT_DEPT_NAME) ?? treeData.value[0]
  currentNodeId.value = target.id
  expandedKeys.value = [...collectParentIds(treeData.value, target.id), target.id]
  await searchByDeptCode(target.full_code)
})
</script>

<template>
  <div class="attendance-page">
    <!-- 筛选栏 -->
    <div class="filter-bar">
      <div class="filter-left">
        <el-date-picker
          v-model="month"
          type="month"
          value-format="YYYY-MM"
          format="YYYY-MM"
          placeholder="选择月份"
          :editable="false"
          style="width: 150px"
        />
        <el-input v-model="nameFilter" placeholder="输入姓名筛选" clearable style="width: 200px" />
        <el-button type="primary" @click="searchByFilter">全局查询</el-button>
        <el-button @click="openDailyRanking">当月排行前100</el-button>
      </div>
    </div>

    <div class="main-wrap">
      <!-- 组织架构 -->
      <div class="left-tree">
        <div class="tree-title">组织架构</div>
        <div class="tree-scroll">
          <el-tree
            v-if="treeData.length"
            :data="treeData"
            :props="treeProps"
            node-key="id"
            highlight-current
            :current-node-key="currentNodeId"
            :default-expanded-keys="expandedKeys"
            :expand-on-click-node="false"
            @node-click="onNodeClick"
          />
        </div>
      </div>

      <!-- 汇总表 -->
      <div class="right-table">
        <div class="table-summary">当前共 <strong>{{ sumList.length }}</strong> 条人员数据</div>
        <el-table
          :data="sumList"
          height="100%"
          border
          stripe
          highlight-current-row
          :default-sort="{ prop: 'overtimeDuration', order: 'descending' }"
        >
          <el-table-column label="排名" width="70" fixed>
            <template #default="{ $index }">{{ $index + 1 }}</template>
          </el-table-column>
          <el-table-column label="头像" width="72">
            <template #default="{ row }">
              <el-image
                v-if="row.avatar"
                :src="row.avatar"
                :preview-src-list="[row.avatar]"
                fit="cover"
                preview-teleported
                hide-on-click-modal
                class="avatar-cell"
                style="width: 36px; height: 36px; border-radius: 50%"
              />
            </template>
          </el-table-column>
          <el-table-column label="姓名" width="110">
            <template #default="{ row }">
              <span class="link-name" @click="openDetail(row.userName, '', row.ddUserId ?? '')">{{ row.userName }}</span>
            </template>
          </el-table-column>
          <el-table-column label="部门" min-width="140">
            <template #default="{ row }">{{ row.deptName || '-' }}</template>
          </el-table-column>
          <el-table-column label="入职日期" width="120">
            <template #default="{ row }">{{ row.hiredDate || '-' }}</template>
          </el-table-column>
          <el-table-column prop="overtimeDuration" label="加班时长" width="120" sortable>
            <template #default="{ row }">
              <span class="overtime-val">{{ row.overtimeDuration }}</span>
            </template>
          </el-table-column>
          <el-table-column prop="workDuration" label="在岗时长" width="120" sortable />
          <el-table-column prop="leaveDuration" label="请假时长" width="120" sortable />
          <el-table-column prop="travelDuration" label="出差时长" width="120" sortable />
          <el-table-column prop="actualDuration" label="实际出勤" width="120" sortable />
          <el-table-column label="员工状态" width="90">
            <template #default="{ row }">{{ statusText(row.employeeStatus) }}</template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <!-- 明细弹层 -->
    <CommonDialog v-model="detailVisible" :title="`${detailName} 每日打卡明细（共${detailData.length}条）`" width="960px" top="6vh" class="stretch-dialog">
      <div class="dialog-table-wrap" v-loading="detailLoading">
      <el-table
        :data="detailData"
        border
        stripe
        height="100%"
        highlight-current-row
        :default-sort="{ prop: 'workDate', order: 'descending' }"
      >
        <el-table-column prop="workDate" label="日期" width="120" sortable />
        <el-table-column prop="overtimeDuration" label="加班时长" width="118" sortable>
          <template #default="{ row }">
            <span class="overtime-val">{{ row.overtimeDuration }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="workDuration" label="在岗时长" width="118" sortable />
        <el-table-column prop="leaveDuration" label="请假时长" width="118" sortable />
        <el-table-column prop="travelDuration" label="出差时长" width="118" sortable />
        <el-table-column prop="actualDuration" label="实际出勤" width="118" sortable />
        <el-table-column label="上班时段" min-width="120">
          <template #default="{ row }">{{ row.workTime || '-' }}</template>
        </el-table-column>
        <el-table-column label="休息时段" min-width="120">
          <template #default="{ row }">{{ row.restTime || '-' }}</template>
        </el-table-column>
        <template #empty>暂无该员工当月考勤明细</template>
      </el-table>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button type="primary" @click="detailVisible = false">关闭</el-button>
        </div>
      </template>
    </CommonDialog>

    <!-- 排行弹层 -->
    <CommonDialog v-model="rankingVisible" :title="rankingTitle" width="1180px" top="6vh" class="stretch-dialog ranking-dialog">
      <div class="ranking-search-bar">
        <el-input
          v-model="rankingKeyword"
          placeholder="输入姓名或部门名称本地筛选"
          clearable
          style="width: 300px"
        />
      </div>
      <div class="dialog-table-wrap" v-loading="rankingLoading">
      <el-table :data="rankingList" border stripe height="100%" highlight-current-row>
        <el-table-column label="排名" width="70">
          <template #default="{ row, $index }">{{ row.index ?? $index + 1 }}</template>
        </el-table-column>
        <el-table-column label="头像" width="72">
          <template #default="{ row }">
            <el-image
              v-if="row.avatar"
              :src="row.avatar"
              :preview-src-list="[row.avatar]"
              fit="cover"
              preview-teleported
              hide-on-click-modal
              class="avatar-cell"
              style="width: 36px; height: 36px; border-radius: 50%"
            />
          </template>
        </el-table-column>
        <el-table-column label="姓名" width="110">
          <template #default="{ row }">
            <span class="link-name" @click="openDetail(row.userName, row.deptId ?? '', row.ddUserId ?? '')">{{ row.userName }}</span>
          </template>
        </el-table-column>
        <el-table-column label="部门" min-width="200">
          <template #default="{ row }">{{ row.fullDeptName || row.deptName || '-' }}</template>
        </el-table-column>
        <el-table-column label="入职日期" width="120">
          <template #default="{ row }">{{ row.hiredDate || '-' }}</template>
        </el-table-column>
        <el-table-column prop="overtimeDuration" label="加班时长" width="100">
          <template #default="{ row }">
            <span class="overtime-val">{{ row.overtimeDuration }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="workDuration" label="在岗时长" width="100" />
        <el-table-column prop="leaveDuration" label="请假时长" width="100" />
        <el-table-column prop="travelDuration" label="出差时长" width="100" />
        <el-table-column prop="actualDuration" label="实际出勤" width="100" />
        <el-table-column label="员工状态" width="90">
          <template #default="{ row }">{{ statusText(row.employeeStatus) }}</template>
        </el-table-column>
        <template #empty>暂无排行数据</template>
      </el-table>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button type="primary" @click="rankingVisible = false">关闭</el-button>
        </div>
      </template>
    </CommonDialog>
  </div>
</template>
