<script setup lang="ts">
/**
 * 快捷键帮助弹窗：按 ? 或 Ctrl+/ 显示所有可用快捷键。
 */
import { onMounted, onUnmounted, ref } from 'vue'

const visible = ref(false)

const shortcuts = [
  { keys: 'Ctrl + K', desc: '打开命令面板' },
  { keys: 'Ctrl + /', desc: '显示快捷键帮助' },
  { keys: '?', desc: '显示快捷键帮助' },
  { keys: 'Ctrl + Shift + R', desc: '刷新当前页面' },
  { keys: 'Esc', desc: '关闭弹窗 / 取消操作' },
]

function onGlobalKey(e: KeyboardEvent) {
  // Ctrl+/ 或单独的 ? 键
  if (e.key === '?' && !e.ctrlKey && !e.metaKey && !e.altKey) {
    // 排除在输入框中的情况
    const tag = (e.target as HTMLElement)?.tagName
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
    e.preventDefault()
    visible.value = !visible.value
  } else if ((e.ctrlKey || e.metaKey) && e.key === '/') {
    e.preventDefault()
    visible.value = !visible.value
  } else if (e.key === 'Escape' && visible.value) {
    visible.value = false
  }
}

onMounted(() => window.addEventListener('keydown', onGlobalKey))
onUnmounted(() => window.removeEventListener('keydown', onGlobalKey))
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="kbd-overlay" @click.self="visible = false">
      <div class="kbd-dialog">
        <div class="kbd-header">
          <h3>键盘快捷键</h3>
          <button class="kbd-close" @click="visible = false">✕</button>
        </div>
        <div class="kbd-list">
          <div v-for="s in shortcuts" :key="s.keys" class="kbd-row">
            <span class="kbd-desc">{{ s.desc }}</span>
            <span class="kbd-keys">
              <kbd v-for="key in s.keys.split(' + ')" :key="key">{{ key }}</kbd>
            </span>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.kbd-overlay {
  position: fixed;
  inset: 0;
  z-index: 9998;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  animation: kbd-fade 0.15s ease;
}

@keyframes kbd-fade {
  from { opacity: 0; }
  to { opacity: 1; }
}

.kbd-dialog {
  width: 420px;
  background: var(--surface, #fff);
  border-radius: var(--radius-lg, 16px);
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.15), 0 0 0 1px var(--border, #e2e8f0);
  overflow: hidden;
  animation: kbd-slide 0.15s ease;
}

@keyframes kbd-slide {
  from { transform: translateY(-10px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.kbd-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.kbd-header h3 {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  margin: 0;
}

.kbd-close {
  background: none;
  border: none;
  font-size: 16px;
  color: var(--text-sub, #94a3b8);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
  transition: background 0.15s;
}

.kbd-close:hover {
  background: var(--page-bg, #f8fafc);
}

.kbd-list {
  padding: 8px 12px;
}

.kbd-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 8px;
  border-radius: 8px;
}

.kbd-row:hover {
  background: var(--page-bg, #f8fafc);
}

.kbd-desc {
  font-size: 14px;
  color: var(--text-main, #0f172a);
}

.kbd-keys {
  display: flex;
  gap: 4px;
}

.kbd-keys kbd {
  display: inline-block;
  padding: 3px 8px;
  font-size: 12px;
  font-family: inherit;
  background: var(--page-bg, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  color: var(--text-sub, #64748b);
  min-width: 24px;
  text-align: center;
  box-shadow: 0 1px 0 var(--border, #e2e8f0);
}

html.dark .kbd-keys kbd {
  background: #334155;
  border-color: #475569;
  color: #94a3b8;
  box-shadow: 0 1px 0 #475569;
}
</style>
