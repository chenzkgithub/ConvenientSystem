<script setup lang="ts" generic="T extends Record<string, any>">
import { computed, onActivated, ref, useSlots } from 'vue'
import type { ElTable } from 'element-plus'
import { formatDate } from '@/common/formatDate'

/** 通用表格列配置 */
export interface DataTableColumn<T = any> {
  /** 字段名（index/selection 类型可省略） */
  prop?: keyof T | string
  /** 列标题 */
  label: string
  /** 列宽 */
  width?: number | string
  /** 最小列宽 */
  minWidth?: number | string
  /** 对齐方式 */
  align?: 'left' | 'center' | 'right'
  /** 固定列 */
  fixed?: 'left' | 'right' | boolean
  /** 内容超出是否悬浮显示完整内容（默认继承 App.vue 的全局配置：超出即悬浮显示；显式 false 可单独关闭） */
  showOverflowTooltip?: boolean
  /** 是否可排序 */
  sortable?: boolean | 'custom'
  /** 列类型：text=普通文本；tag=标签；date=日期；index=序号；selection=多选 */
  type?: 'text' | 'tag' | 'date' | 'index' | 'selection'
  /** 标签类型，或根据行数据返回类型 */
  tagType?: string | ((row: T) => string)
  /** 标签效果 */
  tagEffect?: 'light' | 'dark' | 'plain'
  /** 日期/文本格式化函数 */
  formatter?: (row: T, value: any) => string
  /** 用于日期列的外部格式化函数（替代 formatter） */
  dateFormatter?: (value: any) => string
  /** 是否使用自定义插槽渲染（插槽名 cell-[prop]） */
  custom?: boolean
  /** 自定义列样式类 */
  className?: string
  /** 空值占位文本（默认 -，以灰字呈现）；仅在最终文本为空时使用 */
  emptyText?: string
}

const props = withDefaults(
  defineProps<{
    /** 列配置 */
    columns: DataTableColumn<T>[]
    /** 表格数据 */
    data: T[]
    /** 加载中 */
    loading?: boolean
    /** 总条数 */
    total?: number
    /** 当前页 */
    page?: number
    /** 每页条数 */
    pageSize?: number
    /** 分页可选条数 */
    pageSizes?: number[]
    /** 是否显示分页 */
    showPagination?: boolean
    /** 分页布局 */
    paginationLayout?: string
    /** 空数据提示 */
    emptyText?: string
    /** 是否显示表格边框 */
    border?: boolean
    /** 是否斑马纹 */
    stripe?: boolean
    /** 表格尺寸 */
    size?: 'small' | 'default' | 'large'
    /** 是否高亮当前行 */
    highlightCurrentRow?: boolean
    /** 行主键 */
    rowKey?: string | ((row: T) => string)
    /** 行类名 */
    rowClassName?: string | ((data: { row: T; rowIndex: number }) => string)
    /** 最大高度 */
    maxHeight?: string | number
    /** 表格高度 */
    height?: string | number
    /** 是否需要操作列 */
    showActions?: boolean
    /** 操作列宽度 */
    actionsWidth?: number | string
    /** 操作列是否固定右侧 */
    actionsFixed?: 'left' | 'right' | boolean
    /** 从 keep-alive 缓存切回时是否自动刷新 */
    refreshOnActivated?: boolean
    /** 紧凑模式：去除筛选区/表格区默认内边距，便于嵌入已有内边距的容器 */
    compact?: boolean
    /** 分页下拉是否传送到 body（嵌入浏览器全屏容器时需设 false，否则下拉会被全屏层遮挡） */
    teleported?: boolean
    /** 是否在筛选区末尾自动渲染「查询 / 重置」按钮（查询触发 search，重置触发 reset） */
    searchable?: boolean
    /** 查询按钮文案 */
    searchText?: string
    /** 重置按钮文案 */
    resetText?: string
    /** 已选中行（配合 selection 列使用，支持 v-model:selected） */
    selected?: T[]
  }>(),
  {
    loading: false,
    total: 0,
    page: 1,
    pageSize: 20,
    pageSizes: () => [10, 20, 50, 100],
    showPagination: true,
    paginationLayout: 'total, prev, pager, next',
    emptyText: '暂无数据',
    border: false,
    stripe: false,
    size: 'default',
    highlightCurrentRow: true,
    showActions: true,
    actionsWidth: 'auto',
    actionsFixed: 'right',
    // keep-alive 切回自动刷新：列表页绝大多数场景都应拿到最新数据，故默认开启。
    // 自带轮询或有未提交编辑态的页面需显式传 :refresh-on-activated="false" 关闭。
    refreshOnActivated: true,
    compact: false,
    teleported: true,
    searchable: false,
    searchText: '查询',
    resetText: '重置',
    selected: () => [],
  }
)

const emit = defineEmits<{
  'update:page': [page: number]
  'update:pageSize': [size: number]
  load: []
  'row-click': [row: T, column: any, event: Event]
  'row-dblclick': [row: T, column: any, event: Event]
  'sort-change': [data: { column: any; prop: string | null; order: 'ascending' | 'descending' | null }]
  'selection-change': [selection: T[]]
  'update:selected': [selection: T[]]
  /** 点击内置「查询」按钮（searchable 开启时） */
  search: []
  /** 点击内置「重置」按钮（searchable 开启时） */
  reset: []
}>()

const slots = useSlots()
const tableRef = ref<InstanceType<typeof ElTable>>()

/** 表格高度：未显式指定 height/max-height 时默认撑满容器，由表格自身出现纵向滚动条，避免页面级滚动条 */
const tableHeight = computed(() => props.height ?? (props.maxHeight != null ? undefined : '100%'))

/** 是否有操作列插槽 */
const hasActionsSlot = computed(() => !!slots.actions)

/** 当前页（内部计算） */
const currentPage = computed({
  get: () => props.page,
  set: (val: number) => {
    emit('update:page', val)
  },
})

/** 每页条数（内部计算） */
const currentPageSize = computed({
  get: () => props.pageSize,
  set: (val: number) => {
    emit('update:pageSize', val)
  },
})

/** 是否显示分页 */
const shouldShowPagination = computed(
  () => props.showPagination && (props.total > currentPageSize.value || currentPageSize.value > 10)
)

/** 操作列宽度 */
const computedActionsWidth = computed(() => {
  if (props.actionsWidth !== 'auto') return props.actionsWidth
  return hasActionsSlot.value ? 180 : 80
})

function onPageChange(p: number) {
  currentPage.value = Number(p) || 1
  emit('load')
}

function onSizeChange(s: number) {
  currentPageSize.value = Number(s) || 20
  currentPage.value = 1
  emit('load')
}

function onRowClick(row: T, column: any, event: Event) {
  emit('row-click', row, column, event)
}

function onRowDblclick(row: T, column: any, event: Event) {
  emit('row-dblclick', row, column, event)
}

function onSortChange(data: { column: any; prop: string | null; order: 'ascending' | 'descending' | null }) {
  emit('sort-change', data)
}

function onSelectionChange(selection: T[]) {
  emit('selection-change', selection)
  emit('update:selected', selection)
}

/** 清空表格勾选（批量操作完成后调用） */
function clearSelection() {
  tableRef.value?.clearSelection()
}

defineExpose({ clearSelection, tableRef })

function getTagType(column: DataTableColumn<T>, row: T): string {
  if (typeof column.tagType === 'function') return column.tagType(row)
  return column.tagType || ''
}

function getCellValue(column: DataTableColumn<T>, row: T): any {
  if (!column.prop) return ''
  return (row as any)[column.prop]
}

function getCellText(column: DataTableColumn<T>, row: T): string {
  const value = getCellValue(column, row)
  if (column.formatter) return column.formatter(row, value)
  if (column.dateFormatter) return column.dateFormatter(value)
  // date 列默认走全站统一的日期格式化，无需各页面重复传 dateFormatter。
  if (column.type === 'date') return formatDate(value)
  if (value == null) return ''
  return String(value)
}

/**
 * 单元格最终文本是否为空（据此统一渲染灰字占位，避免各页面自己写一份横线占位与配套样式）。
 * 注意判的是格式化后的文本：列上自带 formatter 时尊重其输出，只有输出为空串才当作空值。
 */
function isCellEmpty(column: DataTableColumn<T>, row: T): boolean {
  return getCellText(column, row) === ''
}

function getColumnKey(column: DataTableColumn<T>, index: number): string {
  return (column.prop as string) || column.type || `col-${index}`
}

function getSlotName(column: DataTableColumn<T>): string {
  return `cell-${column.prop as string}`
}

if (props.refreshOnActivated) {
  onActivated(() => emit('load'))
}
</script>

<template>
  <div class="common-data-table">
    <!-- 筛选与工具栏 -->
    <div v-if="slots.filters || slots.toolbar || searchable" class="data-table-header" :class="{ compact }">
      <div class="data-table-filters">
        <slot name="filters" />
        <!-- 查询 / 重置属于筛选区（紧跟筛选控件），其余按钮一律归 toolbar -->
        <template v-if="searchable">
          <el-button type="primary" @click="emit('search')">{{ searchText }}</el-button>
          <el-button @click="emit('reset')">{{ resetText }}</el-button>
        </template>
      </div>
      <div v-if="slots.toolbar" class="data-table-toolbar">
        <slot name="toolbar" :selected="selected" />
      </div>
    </div>

    <!-- 表格区域 -->
    <div class="data-table-body" :class="{ compact }" v-loading="loading">
      <el-table
        ref="tableRef"
        :data="data"
        :border="border"
        :stripe="stripe"
        :size="size"
        :highlight-current-row="highlightCurrentRow"
        :row-key="rowKey"
        :row-class-name="rowClassName"
        :max-height="maxHeight"
        :height="tableHeight"
        @row-click="onRowClick"
        @row-dblclick="onRowDblclick"
        @sort-change="onSortChange"
        @selection-change="onSelectionChange"
      >
        <template v-for="(column, index) in columns" :key="getColumnKey(column, index)">
          <!-- 多选列 -->
          <el-table-column
            v-if="column.type === 'selection'"
            type="selection"
            :width="column.width || 55"
            :align="column.align || 'center'"
            :fixed="column.fixed"
          />

          <!-- 序号列 -->
          <el-table-column
            v-else-if="column.type === 'index'"
            type="index"
            :label="column.label"
            :width="column.width || 60"
            :align="column.align || 'center'"
            :fixed="column.fixed"
          />

          <!-- 自定义插槽列 -->
          <el-table-column
            v-else-if="column.custom && slots[getSlotName(column)]"
            :prop="column.prop as string"
            :label="column.label"
            :width="column.width"
            :min-width="column.minWidth"
            :align="column.align"
            :fixed="column.fixed"
            :show-overflow-tooltip="column.showOverflowTooltip"
            :sortable="column.sortable"
            :class-name="column.className"
          >
            <template #default="scope">
              <slot :name="getSlotName(column)" v-bind="scope" />
            </template>
          </el-table-column>

          <!-- 标签列 -->
          <el-table-column
            v-else-if="column.type === 'tag'"
            :prop="column.prop as string"
            :label="column.label"
            :width="column.width"
            :min-width="column.minWidth"
            :align="column.align"
            :fixed="column.fixed"
            :show-overflow-tooltip="column.showOverflowTooltip"
            :sortable="column.sortable"
            :class-name="column.className"
          >
            <template #default="{ row }">
              <span v-if="isCellEmpty(column, row)" class="cell-empty">{{ column.emptyText ?? '-' }}</span>
              <el-tag v-else :type="getTagType(column, row) as any" :effect="column.tagEffect" size="small">
                {{ getCellText(column, row) }}
              </el-tag>
            </template>
          </el-table-column>

          <!-- 普通文本/日期列 -->
          <el-table-column
            v-else
            :prop="column.prop as string"
            :label="column.label"
            :width="column.width"
            :min-width="column.minWidth"
            :align="column.align"
            :fixed="column.fixed"
            :show-overflow-tooltip="column.showOverflowTooltip"
            :sortable="column.sortable"
            :class-name="column.className"
          >
            <template #default="{ row }">
              <span v-if="isCellEmpty(column, row)" class="cell-empty">{{ column.emptyText ?? '-' }}</span>
              <template v-else>{{ getCellText(column, row) }}</template>
            </template>
          </el-table-column>
        </template>

        <!-- 操作列：内容为按钮组，不需要内容悬浮提示（否则浮层会盖住按钮） -->
        <el-table-column
          v-if="showActions && hasActionsSlot"
          label="操作"
          :width="computedActionsWidth"
          :fixed="actionsFixed"
          :align="'center'"
          :show-overflow-tooltip="false"
        >
          <template #default="scope">
            <slot name="actions" v-bind="scope" />
          </template>
        </el-table-column>

        <template #empty>
          <slot name="empty">{{ emptyText }}</slot>
        </template>
      </el-table>
    </div>

    <!-- 分页 -->
    <div v-if="shouldShowPagination" class="data-table-footer" :class="{ compact }">
      <el-pagination
        background
        :layout="paginationLayout"
        :total="total"
        :page-size="currentPageSize"
        :page-sizes="pageSizes"
        :current-page="currentPage"
        :teleported="teleported"
        @current-change="onPageChange"
        @size-change="onSizeChange"
      />
    </div>
  </div>
</template>

<style scoped>
.common-data-table {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.data-table-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px;
  flex-wrap: wrap;
}
/* compact 模式工具栏与表格区左右内边距同为 0，保证按钮与列表左右对齐（与错误日志页一致） */
.data-table-header.compact {
  padding: 0 0 12px;
}
.data-table-filters {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.data-table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}
.data-table-body {
  flex: 1;
  min-height: 0;
  padding: 0 12px;
  display: flex;
  flex-direction: column;
}
.data-table-body.compact {
  padding: 0;
}
.data-table-body :deep(.el-table) {
  flex: 1;
}
.data-table-footer {
  display: flex;
  justify-content: center;
  padding: 10px 12px 12px;
  flex-shrink: 0;
}
.data-table-footer.compact {
  padding: 10px 0 0;
}
/* 空值占位：全站列表统一的灰字横线，不再由各页面自行定义 */
.cell-empty {
  color: #9ca3af;
}
</style>
