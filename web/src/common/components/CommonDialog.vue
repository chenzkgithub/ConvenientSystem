<script setup lang="ts">
/**
 * 通用弹窗组件：基于 el-dialog 封装，标题栏右侧提供 折叠吸底 / 全屏 / 关闭 图标。
 *
 * 内置功能：
 * - 折叠吸底：点击减号图标后弹窗消失并通知父级关闭，底部出现吸底小条；
 *   重新点击打开按钮或点击吸底条均可还原，内容不丢失
 * - 全屏/还原：使用浏览器 Fullscreen API（同 SqlQueryView），FullScreen/Aim 图标切换
 * - 关闭：Close 图标
 * - 拖动/拉伸由全局 dialogFlex.ts 自动增强
 * - 默认 close-on-click-modal=false（防误关）
 * - 弹窗关闭后自动重置全屏/折叠状态，并退出浏览器全屏
 *
 * 用法：
 * <CommonDialog v-model="visible" title="标题" width="600px">
 *   内容
 *   <template #footer>底部</template>
 * </CommonDialog>
 */
import { ref, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { FullScreen, Aim, Close, Minus, Bottom, RefreshRight } from '@element-plus/icons-vue'

const props = withDefaults(defineProps<{
  modelValue?: boolean
  title?: string
  width?: string | number
  closeOnClickModal?: boolean
  appendToBody?: boolean
  destroyOnClose?: boolean
  fullscreen?: boolean
  closeOnPressEscape?: boolean
  alignCenter?: boolean
  class?: string
}>(), {
  modelValue: false,
  title: '',
  width: '600px',
  closeOnClickModal: false,
  appendToBody: true,
  destroyOnClose: false,
  fullscreen: false,
  closeOnPressEscape: true,
  alignCenter: false,
  class: '',
})

const emit = defineEmits<{
  'update:modelValue': [val: boolean]
  'update:fullscreen': [val: boolean]
  open: []
  opened: []
  close: []
  closed: []
}>()

/** 实例唯一标识，用于 append-to-body 后查找弹窗 DOM */
const uid = `cd-${Math.random().toString(36).slice(2, 8)}`

const isFullscreen = ref(false)
const isMinimized = ref(false)

/** 父级重新打开弹窗（modelValue false→true）时，取消折叠/全屏状态 */
watch(() => props.modelValue, (val, oldVal) => {
  if (val && !oldVal) {
    isMinimized.value = false
    isFullscreen.value = false
  }
  if (!val && document.fullscreenElement) {
    document.exitFullscreen()
  }
})

/** 获取弹窗 DOM 元素（el-dialog append-to-body 后需查询） */
function getDialogEl(): HTMLElement | null {
  return document.querySelector(`.el-dialog.${uid}`)
}

/** 全屏切换：浏览器 Fullscreen API（同 SqlQueryView） */
async function toggleFullscreen() {
  if (document.fullscreenElement) {
    await document.exitFullscreen()
  } else {
    const el = getDialogEl()
    if (el) await el.requestFullscreen()
  }
}

function onFsChange() {
  const el = getDialogEl()
  const was = isFullscreen.value
  isFullscreen.value = !!document.fullscreenElement && document.fullscreenElement === el
  if (isFullscreen.value) isMinimized.value = false
  if (was !== isFullscreen.value) emit('update:fullscreen', isFullscreen.value)
}

onMounted(() => {
  document.addEventListener('fullscreenchange', onFsChange)
})

onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', onFsChange)
  if (document.fullscreenElement) document.exitFullscreen()
})

/**
 * 折叠吸底：
 * - 折叠时通知父级关闭（modelValue→false），但 isMinimized 保持 true 显示吸底条
 * - 还原时通知父级打开（modelValue→true），isMinimized 置 false
 * - 父级重新点击按钮打开时 modelValue 从 false→true，watch 自动恢复
 */
function toggleMinimize() {
  if (isMinimized.value) {
    isMinimized.value = false
    emit('update:modelValue', true)
  } else {
    // 折叠前先退出全屏
    if (isFullscreen.value && document.fullscreenElement) document.exitFullscreen()
    isMinimized.value = true
    emit('update:modelValue', false)
  }
}

/** el-dialog 实际可见性：父级 visible 且非折叠时才显示 */
const dialogModel = computed(() => props.modelValue && !isMinimized.value)

const dialogProps = computed(() => ({
  modelValue: dialogModel.value,
  title: props.title,
  width: props.width,
  closeOnClickModal: props.closeOnClickModal,
  appendToBody: props.appendToBody,
  destroyOnClose: props.destroyOnClose,
  // 拖拽由全局 dialogFlex.ts 实现（带按钮过滤）；EP 内置拖拽不过滤标题栏按钮，
  // 且其钳制逻辑会让超高弹窗在按下时瞬间上跳，导致关闭按钮 click 落空需点两次
  draggable: false,
  fullscreen: false, // 不用 EP 内置全屏，改用浏览器 Fullscreen API
  closeOnPressEscape: props.closeOnPressEscape,
  alignCenter: props.alignCenter,
  class: ['common-dialog', uid, props.class],
  showClose: false,
}))

/** el-dialog 的 update:modelValue（ESC、遮罩点击等用户交互） */
function handleDialogUpdate(val: boolean) {
  if (!val) emit('update:modelValue', false)
}

/** 折叠中的 close/closed 事件不向父级传播（不是真正关闭） */
function handleClose() {
  if (isMinimized.value) return
  emit('close')
}

function handleClosed() {
  if (isMinimized.value) return
  // 关闭时退出全屏
  if (document.fullscreenElement) document.exitFullscreen()
  isFullscreen.value = false
  emit('closed')
}

function handleOpen() { emit('open') }
function handleOpened() { emit('opened') }

/** 关闭：同时清除折叠状态 */
function doClose() {
  isMinimized.value = false
  if (document.fullscreenElement) document.exitFullscreen()
  emit('update:modelValue', false)
}
</script>

<template>
  <el-dialog
    v-bind="dialogProps"
    @update:model-value="handleDialogUpdate"
    @open="handleOpen"
    @opened="handleOpened"
    @close="handleClose"
    @closed="handleClosed"
  >
    <template #header>
      <div class="cd-header">
        <span class="cd-title">{{ title }}</span>
        <div class="cd-btns">
          <el-button
            circle size="small"
            :icon="Minus"
            title="折叠"
            class="cd-icon-btn"
            @click.stop="toggleMinimize"
          />
          <el-button
            circle size="small"
            :icon="isFullscreen ? Aim : FullScreen"
            :title="isFullscreen ? '还原' : '全屏'"
            class="cd-icon-btn"
            @click.stop="toggleFullscreen"
          />
          <el-button
            circle size="small"
            :icon="Close"
            title="关闭"
            class="cd-icon-btn"
            @click.stop="doClose"
          />
        </div>
      </div>
    </template>

    <slot />

    <template v-if="$slots.footer" #footer>
      <slot name="footer" />
    </template>
  </el-dialog>

  <!-- 折叠吸底条：仅 isMinimized 为 true 时显示 -->
  <Teleport to="body">
    <div v-if="isMinimized" class="cd-minibar" @click="toggleMinimize">
      <span class="cd-minibar-icon"><el-icon :size="14"><Bottom /></el-icon></span>
      <span class="cd-minibar-title">{{ title }}</span>
      <el-button
        circle size="small"
        :icon="RefreshRight"
        title="还原"
        class="cd-minibar-btn"
        @click.stop="toggleMinimize"
      />
      <el-button
        circle size="small"
        :icon="Close"
        title="关闭"
        class="cd-minibar-btn"
        @click.stop="doClose"
      />
    </div>
  </Teleport>
</template>

<style>
/* CommonDialog 样式（append-to-body 后 scoped 失效，用非 scoped） */
.common-dialog .cd-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-right: 4px;
}
.common-dialog .cd-title {
  font-size: 16px;
  font-weight: 600;
  line-height: 24px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.common-dialog .cd-btns {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.common-dialog .cd-icon-btn {
  opacity: 0.75;
  transition: opacity 0.2s;
}
.common-dialog .cd-icon-btn:hover {
  opacity: 1;
}

/* ── 浏览器全屏状态（Fullscreen API :fullscreen 伪类） ── */
.common-dialog:fullscreen {
  width: 100vw !important;
  max-width: 100vw !important;
  margin: 0 !important;
  margin-top: 0 !important;
  height: 100vh !important;
  max-height: 100vh !important;
  border-radius: 0 !important;
  display: flex !important;
  flex-direction: column !important;
  background: #fff !important;
}
.common-dialog:fullscreen .el-dialog__header {
  flex-shrink: 0;
}
.common-dialog:fullscreen .el-dialog__body {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
.common-dialog:fullscreen .el-dialog__footer {
  flex-shrink: 0;
}
/* 隐藏全屏时的遮罩层（弹窗已撑满整个屏幕，遮罩无意义） */
.el-overlay-dialog:has(.common-dialog:fullscreen) {
  background: transparent !important;
}

/* 折叠吸底条 */
.cd-minibar {
  position: fixed;
  bottom: 0;
  right: 24px;
  z-index: 99998;
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 360px;
  height: 40px;
  padding: 0 12px 0 16px;
  background: #fff;
  color: #3b82f6;
  border-radius: var(--radius) var(--radius) 0 0;
  border: 1px solid rgba(59, 130, 246, 0.25);
  border-bottom: none;
  box-shadow: 0 -2px 16px rgba(59, 130, 246, 0.15);
  cursor: pointer;
  user-select: none;
  transition: box-shadow 0.2s, transform 0.2s;
}
.cd-minibar:hover {
  box-shadow: 0 -4px 20px rgba(59, 130, 246, 0.25);
  transform: translateY(-2px);
}
.cd-minibar-icon {
  display: flex;
  align-items: center;
}
.cd-minibar-title {
  font-size: 13px;
  font-weight: 600;
  color: #3b82f6;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}
.cd-minibar-btn {
  opacity: 0.9 !important;
  transition: opacity 0.2s, transform 0.15s;
  background: #fff !important;
  border-color: #3b82f6 !important;
}
.cd-minibar-btn:hover {
  opacity: 1 !important;
  transform: scale(1.05);
}
.cd-minibar .cd-minibar-btn .el-icon,
.cd-minibar .cd-minibar-icon .el-icon {
  color: #3b82f6;
}
</style>
