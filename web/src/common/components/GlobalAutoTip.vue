<script setup lang="ts">
/**
 * 全局自动悬浮提示（浮层载体）：整个系统只挂一个实例（App.vue），用它服务所有界面。
 * 配合 common/globalTip.ts 的解析规则，任何页面的原生 title 与被省略号截断的文本，
 * 都会自动弹出与 CommonTooltip 完全一致的统一浮层，页面侧无需写任何代码。
 *
 * 实现要点：
 * - 用 virtual-triggering + virtual-ref 把悬浮到的元素当作浮层锚点。Element Plus 的
 *   popper/src/trigger.vue 里 virtualRef 是被 watch 的，变更时会把 mouseenter/mouseleave
 *   等监听从旧元素解绑再绑到新元素，所以一个实例就能在整个页面间来回复用
 *   （表格单元格的溢出提示也是这个套路）；
 * - 换锚点时鼠标已经在元素里了、不会再有 mouseenter，需要手动调一次暴露出来的 onOpen()，
 *   由它按 showAfter 延迟打开；之后的关闭、移入保持、自动消失全部走组件自己的逻辑；
 * - 时间与交互参数必须与 CommonTooltip.vue、App.vue 的表格全局配置三处保持一致；
 * - 页面进入浏览器全屏时，浮层必须改挂到全屏元素里（append-to），
 *   否则挂在 body 上的浮层在全屏下根本不会渲染出来。
 */
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import type { TooltipInstance } from 'element-plus'
import { resolveTipTarget } from '@/common/globalTip'

const tipRef = ref<TooltipInstance>()
/** 当前浮层锚点；shallowRef 避免把 DOM 节点做成深层响应式 */
const triggerEl = shallowRef<HTMLElement>()
const content = ref('')

/** 浮层挂载容器：全屏时换成全屏元素，否则浮层在全屏下看不见 */
const appendTo = shallowRef<HTMLElement>(document.body)

function syncAppendTo() {
  const fs = document.fullscreenElement
  appendTo.value = fs instanceof HTMLElement ? fs : document.body
}

function close() {
  tipRef.value?.hide()
  triggerEl.value = undefined
}

async function onMouseOver(e: MouseEvent) {
  const hit = resolveTipTarget(e.target)
  if (!hit) {
    // 锚点元素被列表刷新等操作移出 DOM 时不会再有 mouseleave，浮层会卡在原处，这里兜底关掉
    if (triggerEl.value && !triggerEl.value.isConnected) close()
    return
  }
  if (hit.el === triggerEl.value) {
    // 还是同一个元素：hover 监听已经绑在它身上，再次悬浮由 Element Plus 自己按 showAfter 弹出
    content.value = hit.content
    return
  }
  // 先立刻收起，免得浮层带着上一条内容“滑”到新元素上；换锚点后重新走一遍 0.5s 延迟
  tipRef.value?.hide()
  content.value = hit.content
  triggerEl.value = hit.el
  await nextTick()
  if (triggerEl.value?.isConnected) tipRef.value?.onOpen()
}

function onScroll(e: Event) {
  // 浮层内容层自身滚动不算页面滚动，不能收起，否则提示内容一滚就没了
  if (e.target instanceof HTMLElement && e.target.closest('.el-popper')) return
  if (triggerEl.value) close()
}

onMounted(() => {
  // 捕获阶段监听：个别组件会在冒泡途中 stopPropagation
  document.addEventListener('mouseover', onMouseOver, true)
  document.addEventListener('scroll', onScroll, { capture: true, passive: true })
  document.addEventListener('fullscreenchange', syncAppendTo)
  syncAppendTo()
})

onBeforeUnmount(() => {
  document.removeEventListener('mouseover', onMouseOver, true)
  document.removeEventListener('scroll', onScroll, true)
  document.removeEventListener('fullscreenchange', syncAppendTo)
})
</script>

<template>
  <!-- virtual-triggering 下默认插槽不渲染任何内容，挂在页面上不占位 -->
  <el-tooltip
    ref="tipRef"
    virtual-triggering
    :virtual-ref="triggerEl"
    :append-to="appendTo"
    :content="content"
    placement="top"
    popper-class="cs-tip cs-tip-copyable"
    effect="light"
    :offset="0"
    :show-after="500"
    :auto-close="5000"
    :enterable="true"
    :hide-after="300"
  >
    <template #content>
      <div class="cs-tip-body">{{ content }}</div>
    </template>
  </el-tooltip>
</template>
