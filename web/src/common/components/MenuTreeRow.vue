<script setup lang="ts">
import { inject, computed } from 'vue'
import { Folder, Document, Collection, Rank, MoreFilled, Link, ArrowRight, ArrowDown } from '@element-plus/icons-vue'
import type { MenuNode } from '@/common/types'

const props = defineProps<{
  node: MenuNode
  path: number[]
  depth: number
}>()

/** 从父级注入的菜单操作集合 */
interface MenuActions {
  hasChildren: (node: MenuNode) => boolean
  isEditable: (node: MenuNode) => boolean
  toggleEnabled: (path: number[], val: boolean) => void
  addChildMenu: (parentPath: number[]) => void
  editMenu: (path: number[]) => void
  deleteMenu: (path: number[]) => void
  openMoveDialog: (path: number[]) => void
  select: (path: number[]) => void
  isSelected: (path: number[]) => boolean
  isKeywordHit: (node: MenuNode) => boolean
  isRowExpanded: (node: MenuNode, path: number[]) => boolean
  toggleExpand: (node: MenuNode, path: number[]) => void
  dragState: {
    dragPath: number[] | null
    dragOverPath: number[] | null
    dropPosition: 'before' | 'after' | null
  }
  onDragStart: (path: number[], e: DragEvent) => void
  onDragOver: (path: number[], e: DragEvent) => void
  onDragLeave: (path: number[]) => void
  onDrop: (path: number[], e: DragEvent) => void
  onDragEnd: () => void
}

const actions = inject<MenuActions>('menuActions')!

const isFolder = computed(() => actions.hasChildren(props.node))
const editable = computed(() => actions.isEditable(props.node))
const expanded = computed(() => actions.isRowExpanded(props.node, props.path))
const selected = computed(() => actions.isSelected(props.path))
const keywordHit = computed(() => actions.isKeywordHit(props.node))
const enabled = computed(() => props.node.enabled !== false)

/** 行内图标类型：分组（有子级）/ 外链叶子 / 内部叶子 / 空分组（无地址无子级） */
const iconKind = computed<'folder' | 'external' | 'leaf' | 'group'>(() => {
  if (isFolder.value) return 'folder'
  if (props.node.external) return 'external'
  if (!props.node.page) return 'group'
  return 'leaf'
})

/** 行内元信息（紧跟标题）：内部菜单展示「路由地址 · 路由名称 · 组件短路径」，外链展示完整 URL；无则 null */
const metaLine = computed<string | null>(() => {
  const n = props.node
  if (n.external && n.page) return n.page
  const parts: string[] = []
  if (n.page) parts.push(n.page)
  if (n.name) parts.push(n.name)
  const comp = (n.component || '').replace(/^\/src\//, '')
  if (comp) parts.push(comp)
  return parts.length ? parts.join(' · ') : null
})

/** 分组子菜单数（紧跟标题的小药丸） */
const childCount = computed(() => props.node.children?.length ?? 0)

function onCommand(cmd: string | number | object) {
  const c = String(cmd)
  if (c === 'add') actions.addChildMenu(props.path)
  else if (c === 'edit') actions.editMenu(props.path)
  else if (c === 'move') actions.openMoveDialog(props.path)
  else if (c === 'delete') actions.deleteMenu(props.path)
}

function onToggleExpand() {
  if (isFolder.value) actions.toggleExpand(props.node, props.path)
}

/** 双击整行展开/折叠（开关、按钮、下拉等交互控件上不触发） */
function onDblClick(e: MouseEvent) {
  const target = e.target as HTMLElement
  if (target.closest('button') || target.closest('.el-switch') || target.closest('.expand-toggle') || target.closest('.el-dropdown')) return
  if (isFolder.value) actions.toggleExpand(props.node, props.path)
}

function pathEqual(a: number[] | null, b: number[]): boolean {
  if (!a) return false
  return a.length === b.length && a.every((v, i) => v === b[i])
}

const isDragging = computed(() => pathEqual(actions.dragState.dragPath, props.path))
const isDropTarget = computed(() => pathEqual(actions.dragState.dragOverPath, props.path))
const dropBefore = computed(() => isDropTarget.value && actions.dragState.dropPosition === 'before')
const dropAfter = computed(() => isDropTarget.value && actions.dragState.dropPosition === 'after')
</script>

<template>
  <li class="tree-item">
    <div
      class="row-wrap"
      :class="{
        'is-dragging': isDragging,
        'is-selected': selected,
        'drop-before': dropBefore,
        'drop-after': dropAfter,
      }"
      :style="{ '--depth': depth }"
      draggable="true"
      @click="actions.select(props.path)"
      @dblclick="onDblClick"
      @dragstart="actions.onDragStart(props.path, $event)"
      @dragover="actions.onDragOver(props.path, $event)"
      @dragleave="actions.onDragLeave(props.path)"
      @drop="actions.onDrop(props.path, $event)"
      @dragend="actions.onDragEnd()"
    >
      <!-- 单行：展开箭头 + 手柄 + 图标 + 标题 + 计数 + 路由信息 + 状态标 + 开关 + 更多 -->
      <div class="row-line row-main">
        <span class="expand-toggle" :class="{ leaf: !isFolder }" @click.stop="onToggleExpand">
          <el-icon v-if="isFolder" :size="12">
            <ArrowDown v-if="expanded" />
            <ArrowRight v-else />
          </el-icon>
        </span>

        <span class="drag-handle" title="拖动排序">
          <el-icon :size="14"><Rank /></el-icon>
        </span>

        <span class="row-icon" :class="`tint-${iconKind}`">
          <el-icon :size="14">
            <Folder v-if="iconKind === 'folder'" />
            <Link v-else-if="iconKind === 'external'" />
            <Collection v-else-if="iconKind === 'group'" />
            <Document v-else />
          </el-icon>
        </span>

        <span class="row-title" :class="{ hit: keywordHit }">{{ node.title }}</span>

        <span v-if="isFolder && childCount" class="row-count" :title="`${childCount} 个子菜单`">{{ childCount }}</span>

        <span v-if="metaLine" class="row-meta" :title="metaLine">{{ metaLine }}</span>

        <span v-if="!enabled" class="row-tag tag-gray">已停用</span>
        <span v-if="!isFolder && !node.page" class="row-tag tag-green">分组</span>

        <el-switch
          class="row-switch"
          :model-value="enabled"
          :disabled="!editable"
          size="small"
          inline-prompt
          active-text="启"
          inactive-text="停"
          @click.stop
          @change="(val: string | number | boolean) => actions.toggleEnabled(props.path, !!val)"
        />

        <el-dropdown
          class="row-more"
          trigger="click"
          @command="onCommand"
          @click.stop
        >
          <el-button text size="small" class="more-btn" title="更多操作">
            <el-icon :size="16"><MoreFilled /></el-icon>
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item v-if="editable" command="add">新增子级</el-dropdown-item>
              <el-dropdown-item v-if="editable" command="edit">编辑</el-dropdown-item>
              <el-dropdown-item v-if="editable" command="move">移动到…</el-dropdown-item>
              <el-dropdown-item v-if="editable" command="delete" divided>
                <span class="danger-text">删除</span>
              </el-dropdown-item>
              <el-dropdown-item v-if="!editable" disabled>该菜单已被锁定编辑</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </div>
    </div>

    <!-- 子级递归（收起时不渲染） -->
    <ul v-if="isFolder && expanded" class="menu-tree sub">
      <MenuTreeRow
        v-for="(child, cidx) in node.children"
        :key="cidx"
        :node="child"
        :path="[...props.path, cidx]"
        :depth="props.depth + 1"
      />
    </ul>
  </li>
</template>

<style scoped>
.tree-item {
  list-style: none;
  margin: 0;
}

/* 两行整体包裹：hover/选中/拖拽视觉统一作用于整个卡片 */
.row-wrap {
  position: relative;
  margin: 2px 4px;
  border-radius: var(--radius-sm, 8px);
  border: 1px solid transparent;
  cursor: pointer;
  user-select: none;
  transition: background 0.15s ease, border-color 0.15s ease;
}

.row-wrap:hover {
  background: var(--page-bg, #f8fafc);
}

.row-wrap.is-selected {
  background: var(--brand-50, #eff6ff);
  border-color: var(--brand-200, #bfdbfe);
}

/* 嵌套引导线 */
.row-wrap::before {
  content: '';
  position: absolute;
  left: calc(var(--depth, 0) * 24px + 20px);
  top: 0;
  bottom: 0;
  width: 1px;
  background: var(--border, #e2e8f0);
  pointer-events: none;
}

.row-wrap:hover::before,
.row-wrap.is-selected::before {
  display: none;
}

.row-line {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-left: calc(var(--depth, 0) * 24px + 12px);
  padding-right: 10px;
}

.row-main {
  height: 38px;
}

/* 展开箭头 */
.expand-toggle {
  width: 18px;
  height: 18px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-sub, #64748b);
  border-radius: 4px;
  flex-shrink: 0;
  cursor: pointer;
}

.expand-toggle:hover {
  background: var(--border, #e2e8f0);
  color: var(--text-main, #0f172a);
}

.expand-toggle.leaf {
  cursor: default;
}

.expand-toggle.leaf:hover {
  background: transparent;
  color: var(--text-sub, #64748b);
}

/* 拖拽手柄 */
.drag-handle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  color: var(--text-sub, #64748b);
  cursor: grab;
  opacity: 0.25;
  transition: opacity 0.15s;
  flex-shrink: 0;
}

.row-wrap:hover .drag-handle {
  opacity: 1;
}

.drag-handle:active {
  cursor: grabbing;
}

/* 类型图标：扁平柔色 chip（分组蓝 / 外链琥珀 / 内部叶子石板灰 / 空分组浅灰） */
.row-icon {
  width: 24px;
  height: 24px;
  border-radius: 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.row-icon.tint-folder { background: #eff6ff; color: #2563eb; }
.row-icon.tint-external { background: #fffbeb; color: #d97706; }
.row-icon.tint-leaf { background: #f1f5f9; color: #475569; }
.row-icon.tint-group { background: #f8fafc; color: #94a3b8; }

html.dark .row-icon.tint-folder { background: rgba(59, 130, 246, 0.16); color: #7db9ff; }
html.dark .row-icon.tint-external { background: rgba(245, 158, 11, 0.16); color: #fbbf24; }
html.dark .row-icon.tint-leaf { background: rgba(100, 116, 139, 0.2); color: #94a3b8; }
html.dark .row-icon.tint-group { background: rgba(148, 163, 184, 0.12); color: #64748b; }

/* 标题 */
.row-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-main, #0f172a);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.row-title.hit {
  color: var(--brand, #3b82f6);
  font-weight: 600;
}

/* 分组子菜单计数：紧跟标题的小药丸 */
.row-count {
  font-size: 11px;
  line-height: 16px;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--page-bg, #f8fafc);
  color: var(--text-sub, #64748b);
  flex-shrink: 0;
}

html.dark .row-count {
  background: rgba(148, 163, 184, 0.15);
  color: #94a3b8;
}

/* 状态标（只保留停用/分组两个轻量标记；外链身份由标题前图标表达） */
.row-tag {
  font-size: 11px;
  padding: 1px 7px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
}

.tag-gray {
  color: var(--text-sub, #64748b);
  background: var(--page-bg, #f8fafc);
}

.tag-green {
  color: #16a34a;
  background: #f0fdf4;
}

/* 行内元信息：路由地址 · 路由名称 · 组件短路径（紧跟标题，占满剩余宽度，超长省略，悬浮看全文） */
.row-meta {
  flex: 1;
  min-width: 0;
  font-size: 12px;
  color: var(--text-sub, #64748b);
  font-family: 'SF Mono', 'Cascadia Code', Consolas, monospace;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* 开关 */
.row-switch {
  flex-shrink: 0;
  margin-left: auto;
}

.row-switch :deep(.el-switch__core) {
  height: 18px;
}

.row-switch :deep(.el-switch__inner) {
  font-size: 10px;
}

/* 更多按钮 */
.row-more {
  flex-shrink: 0;
}

.more-btn {
  padding: 4px;
  margin: 0;
  color: var(--text-sub, #64748b);
  opacity: 0;
  transition: opacity 0.15s;
}

.row-wrap:hover .more-btn {
  opacity: 1;
}

.danger-text {
  color: var(--el-color-danger);
}

/* 拖拽视觉反馈 */
.row-wrap.is-dragging {
  background: var(--brand-50, #eff6ff);
}

.row-wrap.drop-before,
.row-wrap.drop-after {
  background: var(--brand-50, #eff6ff);
}

/* 蓝色放置指示线 */
.row-wrap.drop-before::after,
.row-wrap.drop-after::after {
  content: '';
  position: absolute;
  left: 8px;
  right: 8px;
  height: 3px;
  border-radius: 3px;
  background: var(--brand, #3b82f6);
  box-shadow: 0 0 6px 1px rgba(59, 130, 246, 0.5);
  z-index: 1;
  pointer-events: none;
}

.row-wrap.drop-before::after {
  top: -2px;
}

.row-wrap.drop-after::after {
  bottom: -2px;
}

/* 子树 */
.menu-tree.sub {
  list-style: none;
  padding: 0;
  margin: 0;
}
</style>
