<script setup lang="ts">
/**
 * 命令面板（Ctrl/Cmd + K）：快速搜索并跳转菜单页面。
 * 类似 VSCode / Linear 的命令面板体验。
 */
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Search } from '@element-plus/icons-vue'
import { useMenuStore } from '@/common/stores/menu'
import { toMenuLocation } from '@/common/menuLink'
import { pinyinMatchIndex } from '@/common/pinyin'
import type { MenuNode } from '@/common/types'

const menuStore = useMenuStore()
const router = useRouter()

const visible = ref(false)
const keyword = ref('')
const activeIdx = ref(0)
const inputRef = ref<HTMLInputElement | null>(null)

interface CmdItem {
  title: string
  group: string
  page: string
  leaf: MenuNode
}

/** 搜索所有叶子菜单：标题、分组名、路径任一命中 */
const items = computed<CmdItem[]>(() => {
  const kw = keyword.value.trim().toLowerCase()
  const all: CmdItem[] = []
  const walk = (nodes: MenuNode[], groupPath: string[]) => {
    for (const n of nodes) {
      if (n.visible === false || n.enabled === false) continue
      if (Array.isArray(n.children) && n.children.length > 0) {
        walk(n.children, [...groupPath, n.title])
      } else if (n.page) {
        const group = groupPath.join(' / ')
        if (!kw) {
          all.push({ title: n.title, group, page: n.page, leaf: n })
        } else {
          const t = n.title.toLowerCase()
          const g = group.toLowerCase()
          const p = (n.page || '').toLowerCase()
          if (
            t.includes(kw) || g.includes(kw) || p.includes(kw) ||
            pinyinMatchIndex(n.title, kw) >= 0 || pinyinMatchIndex(group, kw) >= 0
          ) {
            all.push({ title: n.title, group, page: n.page, leaf: n })
          }
        }
      }
    }
  }
  walk(menuStore.menus, [])
  return all.slice(0, 20) // 最多显示 20 条
})

watch(keyword, () => { activeIdx.value = 0 })
watch(visible, (v) => {
  if (v) {
    keyword.value = ''
    activeIdx.value = 0
    nextTick(() => inputRef.value?.focus())
  }
})

function open() { visible.value = true }
function close() { visible.value = false }

function go(item: CmdItem) {
  close()
  void router.push(toMenuLocation(item.leaf))
}

function onKeydown(e: KeyboardEvent) {
  if (!visible.value) return
  if (e.key === 'Escape') { e.preventDefault(); close() }
  else if (e.key === 'ArrowDown') { e.preventDefault(); activeIdx.value = Math.min(activeIdx.value + 1, items.value.length - 1) }
  else if (e.key === 'ArrowUp') { e.preventDefault(); activeIdx.value = Math.max(activeIdx.value - 1, 0) }
  else if (e.key === 'Enter') {
    e.preventDefault()
    const item = items.value[activeIdx.value]
    if (item) go(item)
  }
}

function onGlobalKey(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
    e.preventDefault()
    visible.value ? close() : open()
  }
}

onMounted(() => {
  window.addEventListener('keydown', onGlobalKey)
  window.addEventListener('keydown', onKeydown)
})
onUnmounted(() => {
  window.removeEventListener('keydown', onGlobalKey)
  window.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="cmd-overlay" @click.self="close">
      <div class="cmd-dialog">
        <div class="cmd-input-wrap">
          <el-icon class="cmd-input-icon" :size="18"><Search /></el-icon>
          <input
            ref="inputRef"
            v-model="keyword"
            class="cmd-input"
            placeholder="搜索页面、菜单、功能..."
            autocomplete="off"
            spellcheck="false"
          />
          <kbd class="cmd-kbd">ESC</kbd>
        </div>
        <div class="cmd-results">
          <div v-if="items.length === 0" class="cmd-empty">
            没有匹配的结果
          </div>
          <div
            v-for="(item, idx) in items"
            :key="item.page"
            class="cmd-item"
            :class="{ active: idx === activeIdx }"
            @click="go(item)"
            @mouseenter="activeIdx = idx"
          >
            <div class="cmd-item-icon">📄</div>
            <div class="cmd-item-body">
              <span class="cmd-item-title">{{ item.title }}</span>
              <span v-if="item.group" class="cmd-item-group">{{ item.group }}</span>
            </div>
            <el-icon v-if="idx === activeIdx" class="cmd-item-arrow">→</el-icon>
          </div>
        </div>
        <div class="cmd-footer">
          <span><kbd class="cmd-kbd-sm">↑</kbd><kbd class="cmd-kbd-sm">↓</kbd> 导航</span>
          <span><kbd class="cmd-kbd-sm">↵</kbd> 打开</span>
          <span><kbd class="cmd-kbd-sm">esc</kbd> 关闭</span>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.cmd-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  animation: cmd-fade-in 0.15s ease;
}

@keyframes cmd-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

.cmd-dialog {
  width: 560px;
  max-height: 480px;
  background: var(--surface, #fff);
  border-radius: var(--radius-lg, 16px);
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.15), 0 0 0 1px var(--border, #e2e8f0);
  overflow: hidden;
  display: flex;
  flex-direction: column;
  animation: cmd-slide-in 0.15s ease;
}

@keyframes cmd-slide-in {
  from { transform: translateY(-10px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.cmd-input-wrap {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.cmd-input-icon {
  color: var(--text-sub, #64748b);
  flex-shrink: 0;
}

.cmd-input {
  flex: 1;
  border: none;
  outline: none;
  background: transparent;
  font-size: 16px;
  color: var(--text-main, #0f172a);
  font-family: inherit;
}

.cmd-input::placeholder {
  color: var(--text-sub, #94a3b8);
}

.cmd-results {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}

.cmd-empty {
  padding: 32px 16px;
  text-align: center;
  color: var(--text-sub, #94a3b8);
  font-size: 14px;
}

.cmd-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: var(--radius-sm, 8px);
  cursor: pointer;
  transition: background 0.1s;
}

.cmd-item:hover,
.cmd-item.active {
  background: var(--brand-50, #eff6ff);
}

html.dark .cmd-item:hover,
html.dark .cmd-item.active {
  background: #2d3748;
}

.cmd-item-icon {
  font-size: 16px;
  flex-shrink: 0;
  width: 24px;
  text-align: center;
}

.cmd-item-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.cmd-item-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-main, #0f172a);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.cmd-item-group {
  font-size: 12px;
  color: var(--text-sub, #94a3b8);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.cmd-item-arrow {
  color: var(--text-sub, #94a3b8);
  font-size: 14px;
  flex-shrink: 0;
}

.cmd-footer {
  display: flex;
  gap: 16px;
  padding: 10px 18px;
  border-top: 1px solid var(--border, #e2e8f0);
  font-size: 12px;
  color: var(--text-sub, #94a3b8);
}

.cmd-kbd,
.cmd-kbd-sm {
  display: inline-block;
  padding: 2px 6px;
  font-size: 11px;
  font-family: inherit;
  background: var(--page-bg, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 4px;
  color: var(--text-sub, #64748b);
}

.cmd-kbd-sm {
  padding: 1px 5px;
  margin-right: 2px;
}

html.dark .cmd-kbd,
html.dark .cmd-kbd-sm {
  background: #334155;
  border-color: #475569;
  color: #94a3b8;
}
</style>
