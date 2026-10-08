<script setup lang="ts">
import { nextTick, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { verifyUnlock } from '@/common/api/lock'
import { useLockStore } from '@/common/stores/lock'
import { loadLocalImage } from '@/common/utils/localImage'

const lock = useLockStore()
const emit = defineEmits<{ (e: 'unlocked'): void }>()

const password = ref('')
const tip = ref('输入密码解锁页面')
const tipError = ref(false)
const inputRef = ref()

// 锁屏出现时清空并聚焦输入框
watch(
  () => lock.isLocked,
  (locked) => {
    if (locked) {
      password.value = ''
      tip.value = '输入密码解锁页面'
      tipError.value = false
      nextTick(() => inputRef.value?.focus())
    }
  },
  { immediate: true },
)

// 本机自定义锁屏背景（IndexedDB；空串用内置默认图）
const customBg = ref('')
let customBgUrl = '' // 已创建的 object URL，卸载时释放

onMounted(async () => {
  try {
    const blob = await loadLocalImage('lockBg')
    if (blob) {
      customBgUrl = URL.createObjectURL(blob)
      customBg.value = customBgUrl
    }
  } catch { /* 读取失败时使用内置默认背景 */
  }
  document.addEventListener('keydown', onKeydown, true)
  document.addEventListener('contextmenu', onContextmenu, true)
  document.addEventListener('focusin', onFocusIn, true)
  window.addEventListener('focus', focusInput)
  focusInput()
})

onBeforeUnmount(() => {
  if (customBgUrl) URL.revokeObjectURL(customBgUrl)
  document.removeEventListener('keydown', onKeydown, true)
  document.removeEventListener('contextmenu', onContextmenu, true)
  document.removeEventListener('focusin', onFocusIn, true)
  window.removeEventListener('focus', focusInput)
})

/** 拦截刷新与开发者工具快捷键，防止锁屏期间绕过解锁 */
function onKeydown(e: KeyboardEvent) {
  const k = e.key
  // Tab 不允许把焦点切出密码框
  if (k === 'Tab') { e.preventDefault(); focusInput(); return }
  if (k === 'F5') { e.preventDefault(); return }
  if (e.ctrlKey && (k === 'r' || k === 'R' || k === 'F5')) { e.preventDefault(); return }
  if (k === 'F12') { e.preventDefault(); return }
  if (e.ctrlKey && e.shiftKey && (k === 'I' || k === 'i' || k === 'J' || k === 'j' || k === 'C' || k === 'c')) {
    e.preventDefault(); return
  }
  if (e.ctrlKey && (k === 'u' || k === 'U')) { e.preventDefault(); return }
}

function onContextmenu(e: MouseEvent) { e.preventDefault() }

function focusInput() {
  nextTick(() => inputRef.value?.focus())
}

/** 失焦时把焦点抢回密码框；若用户正在点击解锁按钮，则让按钮完成点击，不抢焦点 */
function onInputBlur(e: FocusEvent) {
  const target = e.relatedTarget as HTMLElement | null
  if (target?.closest('button')) return
  focusInput()
}

/** 锁屏期间焦点只允许停留在密码框或解锁按钮上，其他元素获得焦点时立即抢回 */
function onFocusIn(e: FocusEvent) {
  const target = e.target as HTMLElement | null
  if (!target) return
  if (target.tagName === 'INPUT' || target.closest('button')) return
  e.preventDefault?.()
  focusInput()
}

/** 粒子随机位置（固定种子避免每次渲染变化） */
function particleStyle(i: number) {
  const seed = (i * 137.5) % 100
  const left = (seed * 1.7) % 100
  const top = (seed * 2.3 + 10) % 100
  const size = 2 + (i % 3)
  const delay = (i * 0.8) % 6
  const duration = 8 + (i % 5)
  return {
    left: `${left}%`,
    top: `${top}%`,
    width: `${size}px`,
    height: `${size}px`,
    animationDelay: `${delay}s`,
    animationDuration: `${duration}s`,
  }
}

async function doUnlock() {
  try {
    const data = await verifyUnlock(password.value.trim())
    if (data.ok) {
      lock.unlock()
      emit('unlocked')
    } else {
      tipError.value = true
      tip.value = '密码错误，请重新输入'
      inputRef.value?.focus()
    }
  } catch (e) {
    tipError.value = true
    tip.value = '校验失败：' + (e as Error).message
    inputRef.value?.focus()
  }
}
</script>

<template>
  <div class="lock-wrap">
    <!-- 背景图（用户自定义优先，未设置时用主色渐变） -->
    <div class="lock-bg-img" :style="customBg ? { backgroundImage: `url(${customBg})` } : undefined"></div>
    <!-- 白雾遮罩（白底无感；自定义图时把图提亮，保证深色文字可读） -->
    <div class="lock-overlay"></div>
    <!-- 浮动光点装饰 -->
    <div class="lock-particles">
      <span v-for="i in 12" :key="i" class="particle" :style="particleStyle(i)"></span>
    </div>

    <div class="lock-card">
      <!-- 系统 Logo -->
      <div class="lock-logo">
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
          <circle cx="12" cy="12" r="9.5" stroke="#fff" stroke-width="1.4" opacity="0.9" />
          <path d="M7.5 12.5l3 3 6-6.5" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </div>
      <h2>界面已锁定</h2>
      <p class="lock-sub">请输入密码以继续操作</p>
      <el-input
        ref="inputRef"
        v-model="password"
        type="password"
        size="large"
        placeholder="请输入解锁密码"
        show-password
        @keyup.enter="doUnlock"
        @blur="onInputBlur"
      />
      <div class="lock-tip" :class="{ error: tipError }">{{ tip }}</div>
      <el-button type="primary" size="large" style="width: 100%" @click="doUnlock">解 锁</el-button>
    </div>
  </div>
</template>

<style scoped>
.lock-wrap {
  position: fixed;
  inset: 0;
  z-index: 99999;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  background: transparent;
}

/* ── 锁屏背景（默认白色，有自定义图时被行内样式覆盖） ── */
.lock-bg-img {
  position: absolute;
  inset: 0;
  background-color: #fff;
  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;
  /* 背景图轻微缩放避免边缘空白 */
  transform: scale(1.05);
  animation: bgZoom 30s ease-in-out infinite alternate;
}

@keyframes bgZoom {
  from { transform: scale(1.05) translate(0, 0); }
  to { transform: scale(1.1) translate(-10px, -8px); }
}

/* ── 白雾遮罩（白色背景时无感，自定义图时提亮画面） ── */
.lock-overlay {
  position: absolute;
  inset: 0;
  background: rgba(255, 255, 255, 0.45);
  backdrop-filter: blur(0px);
  -webkit-backdrop-filter: blur(0px);
}

/* ── 浮动光点 ── */
.lock-particles {
  position: absolute;
  inset: 0;
  pointer-events: none;
  overflow: hidden;
}
.particle {
  position: absolute;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.6);
  box-shadow: 0 0 6px rgba(255, 255, 255, 0.5);
  animation: floatUp linear infinite;
  opacity: 0;
}
@keyframes floatUp {
  0% { opacity: 0; transform: translateY(20px); }
  20% { opacity: 0.8; }
  80% { opacity: 0.8; }
  100% { opacity: 0; transform: translateY(-40px); }
}

/* ── 锁定卡片：白色毛玻璃 + 淡灰边框，深色文字在白色背景上可读 ── */
.lock-card {
  position: relative;
  z-index: 1;
  background: rgba(255, 255, 255, 0.65);
  border: 1px solid rgba(15, 23, 42, 0.08);
  border-radius: 24px;
  padding: 44px 40px 36px;
  min-width: 380px;
  text-align: center;
  backdrop-filter: blur(6px);
  -webkit-backdrop-filter: blur(6px);
  box-shadow: 0 16px 40px rgba(0, 0, 0, 0.2), inset 0 1px 0 rgba(255, 255, 255, 0.6);
  animation: cardIn 0.5s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes cardIn {
  from { opacity: 0; transform: translateY(24px) scale(0.95); }
  to { opacity: 1; transform: translateY(0) scale(1); }
}

/* ── Logo 圆 ── */
.lock-logo {
  width: 64px;
  height: 64px;
  margin: 0 auto 20px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  box-shadow: 0 0 0 4px rgba(99, 102, 241, 0.2), 0 8px 24px rgba(99, 102, 241, 0.35);
  animation: logoPulse 3s ease-in-out infinite;
}

@keyframes logoPulse {
  0%, 100% { box-shadow: 0 0 0 4px rgba(99, 102, 241, 0.2), 0 8px 24px rgba(99, 102, 241, 0.35); }
  50% { box-shadow: 0 0 0 10px rgba(99, 102, 241, 0.08), 0 8px 32px rgba(99, 102, 241, 0.45); }
}

.lock-card h2 {
  color: #1e293b;
  font-size: 22px;
  margin-bottom: 6px;
  font-weight: 700;
  letter-spacing: 1px;
}

.lock-sub {
  color: #64748b;
  font-size: 13px;
  margin-bottom: 24px;
}

/* ── 输入框样式覆盖（白色实底 + 深色文字） ── */
.lock-card :deep(.el-input__wrapper) {
  background: #fff;
  border: 1px solid rgba(15, 23, 42, 0.12);
  box-shadow: none;
  border-radius: 12px;
  transition: border-color 0.2s, background 0.2s, box-shadow 0.2s;
}
.lock-card :deep(.el-input__wrapper:hover) {
  border-color: rgba(139, 92, 246, 0.5);
  background: #fff;
}
.lock-card :deep(.el-input__wrapper.is-focus) {
  border-color: #8b5cf6;
  background: #fff;
  box-shadow: 0 0 0 3px rgba(139, 92, 246, 0.2);
}
.lock-card :deep(.el-input__inner) {
  color: #1e293b;
}
.lock-card :deep(.el-input__inner::placeholder) {
  color: #94a3b8;
}
.lock-card :deep(.el-input .el-icon) {
  color: #94a3b8;
}

/* ── 解锁按钮 ── */
.lock-card :deep(.el-button--primary) {
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  border: none;
  border-radius: 12px;
  font-weight: 600;
  letter-spacing: 2px;
  box-shadow: 0 4px 16px rgba(139, 92, 246, 0.4);
  transition: filter 0.2s, transform 0.15s, box-shadow 0.2s;
}
.lock-card :deep(.el-button--primary:hover) {
  filter: brightness(1.1);
  transform: translateY(-1px);
  box-shadow: 0 6px 24px rgba(139, 92, 246, 0.5);
}
.lock-card :deep(.el-button--primary:active) {
  transform: translateY(0);
}

/* ── 提示文字 ── */
.lock-tip {
  height: 22px;
  font-size: 14px;
  margin: 12px 0 16px;
  color: #64748b;
  transition: color 0.2s;
}

.lock-tip.error {
  color: #ef4444;
}
</style>
