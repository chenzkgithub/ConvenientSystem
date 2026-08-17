<script setup lang="ts">
import type { MenuNode } from '@/common/types'
import { resolveMenuTarget } from '@/common/menuLink'

defineProps<{ nodes: MenuNode[] }>()

function hasChildren(node: MenuNode): boolean {
  return Array.isArray(node.children) && node.children.length > 0
}

/** 过滤掉 visible=false 的节点 */
function visibleNodes(nodes: MenuNode[]): MenuNode[] {
  return nodes.filter(n => n.visible !== false)
}
</script>

<template>
  <template v-for="(node, idx) in visibleNodes(nodes)" :key="node.page || node.title + idx">
    <el-sub-menu v-if="hasChildren(node)" :index="node.title + '-' + idx">
      <template #title>{{ node.title }}</template>
      <MenuTree :nodes="visibleNodes(node.children)" />
    </el-sub-menu>
    <el-menu-item v-else :index="resolveMenuTarget(node)">{{ node.title }}</el-menu-item>
  </template>
</template>
