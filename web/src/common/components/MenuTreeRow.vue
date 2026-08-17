<script setup lang="ts">
import { inject, computed } from 'vue'
import { Folder, Document, Rank, Edit, Delete, Plus, Position, Link } from '@element-plus/icons-vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import type { MenuNode } from '@/common/types'

const props = defineProps<{
  node: MenuNode
  path: number[]
  depth: number
}>()

/** 从父级注入的菜单操作集合 */
interface MenuActions {
  hasChildren: (node: MenuNode) => boolean
  formatComponent: (path?: string | null) => string
  isEditable: (node: MenuNode) => boolean
  toggleEnabled: (path: number[], val: boolean) => void
  addChildMenu: (parentPath: number[]) => void
  editMenu: (path: number[]) => void
  deleteMenu: (path: number[]) => void
  openMoveDialog: (path: number[]) => void
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
const showGuide = computed(() => props.depth > 0)

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
      class="menu-row"
      :class="{
        'is-dragging': isDragging,
        'drop-before': dropBefore,
        'drop-after': dropAfter,
        'is-group': isFolder,
        'no-guide': !showGuide,
      }"
      :style="{ '--depth': depth }"
      draggable="true"
      @dragstart="actions.onDragStart(props.path, $event)"
      @dragover="actions.onDragOver(props.path, $event)"
      @dragleave="actions.onDragLeave(props.path)"
      @drop="actions.onDrop(props.path, $event)"
      @dragend="actions.onDragEnd()"
    >
      <!-- 拖拽手柄 -->
      <span class="drag-handle" title="拖动排序">
        <el-icon><Rank /></el-icon>
      </span>

      <!-- 类型图标 -->
      <span class="row-icon" :class="isFolder ? 'icon-folder' : 'icon-leaf'">
        <el-icon v-if="isFolder"><Folder /></el-icon>
        <el-icon v-else><Document /></el-icon>
      </span>

      <!-- 标题 -->
      <span class="row-title">{{ node.title }}</span>

      <!-- 徽章 -->
      <CommonTooltip v-if="node.page" :content="node.page">
        <span class="row-badge badge-page">{{ node.page }}</span>
      </CommonTooltip>
      <span v-else class="row-badge badge-group">分组</span>
      <span v-if="node.external" class="row-badge badge-external">
        <el-icon :size="11"><Link /></el-icon>外链
      </span>
      <span v-if="node.name" class="row-badge badge-name">{{ node.name }}</span>
      <CommonTooltip v-if="node.component" :content="node.component">
        <span class="row-badge badge-component">{{ actions.formatComponent(node.component) }}</span>
      </CommonTooltip>

      <!-- 启用开关 -->
      <el-switch
        class="row-switch"
        :model-value="node.enabled !== false"
        :disabled="!editable"
        size="small"
        inline-prompt
        active-text="启"
        inactive-text="停"
        @change="(val: string | number | boolean) => actions.toggleEnabled(props.path, !!val)"
      />

      <!-- 操作按钮 -->
      <span class="row-actions">
        <template v-if="editable">
          <el-button size="small" type="primary" link title="移动到其他父级" @click="actions.openMoveDialog(props.path)">
            <el-icon><Position /></el-icon>
          </el-button>
          <el-button size="small" type="success" link title="新增子菜单" @click="actions.addChildMenu(props.path)">
            <el-icon><Plus /></el-icon>
          </el-button>
          <el-button size="small" type="warning" link title="编辑" @click="actions.editMenu(props.path)">
            <el-icon><Edit /></el-icon>
          </el-button>
          <el-button size="small" type="danger" link title="删除" @click="actions.deleteMenu(props.path)">
            <el-icon><Delete /></el-icon>
          </el-button>
        </template>
      </span>
    </div>

    <!-- 子级递归 -->
    <ul v-if="isFolder" class="menu-tree sub">
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

.menu-row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 40px;
  padding-left: calc(var(--depth, 0) * 24px + 12px);
  padding-right: 12px;
  border-radius: var(--radius-sm, 8px);
  transition: background 0.15s ease;
  position: relative;
  user-select: none;
}

.menu-row:hover {
  background: var(--page-bg, #f8fafc);
}

/* 嵌套引导线 */
.menu-row:not(.no-guide)::before {
  content: '';
  position: absolute;
  left: calc((var(--depth, 0) - 1) * 24px + 18px);
  top: 0;
  bottom: 0;
  width: 1px;
  background: var(--border, #e2e8f0);
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

.menu-row:hover .drag-handle {
  opacity: 1;
}

.drag-handle:active {
  cursor: grabbing;
}

/* 类型图标 */
.row-icon {
  width: 24px;
  height: 24px;
  border-radius: 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
  color: #fff;
}

.icon-folder {
  background: linear-gradient(135deg, #3b82f6, #2563eb);
}

.icon-leaf {
  background: linear-gradient(135deg, #64748b, #475569);
}

/* 标题 */
.row-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-main, #0f172a);
  white-space: nowrap;
}

.menu-row.is-group .row-title {
  font-weight: 600;
}

/* 徽章 */
.row-badge {
  font-size: 11px;
  padding: 1px 8px;
  border-radius: 999px;
  flex-shrink: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

.badge-page {
  color: var(--text-sub, #64748b);
  background: var(--page-bg, #f8fafc);
  font-family: 'SF Mono', 'Cascadia Code', Consolas, monospace;
  max-width: 200px;
}

.badge-group {
  color: #16a34a;
  background: #f0fdf4;
}

.badge-external {
  color: #2563eb;
  background: #eff6ff;
}

.badge-name {
  color: #d97706;
  background: #fffbeb;
}

.badge-component {
  color: #7c3aed;
  background: #f5f3ff;
  max-width: 140px;
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

/* 操作按钮 */
.row-actions {
  display: flex;
  gap: 2px;
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 0.15s;
}

.menu-row:hover .row-actions {
  opacity: 1;
}

.row-actions .el-button {
  padding: 4px;
  margin: 0;
}

/* 拖拽视觉反馈：仅用背景色标记被拖拽行，不加 opacity/outline 避免与原生 ghost 叠加产生虚影 */
.menu-row.is-dragging {
  background: var(--brand-50, #eff6ff);
}

/* 放置目标高亮 */
.menu-row.drop-before,
.menu-row.drop-after {
  background: var(--brand-50, #eff6ff);
}

/* 蓝色指示线 — 用 ::after 避免与引导线 ::before 冲突 */
.menu-row.drop-before::after,
.menu-row.drop-after::after {
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

.menu-row.drop-before::after {
  top: -2px;
}

.menu-row.drop-after::after {
  bottom: -2px;
}

/* 子树 */
.menu-tree.sub {
  list-style: none;
  padding: 0;
  margin: 0;
}
</style>
