<script setup lang="ts">
/**
 * 统一悬浮提示组件：系统内所有悬浮提示（不限于列表）都用它，不要直接写 el-tooltip。
 * 注：大部分提示已由全局自动提示（common/globalTip.ts + components/GlobalAutoTip.vue）兜住——
 * 原生 title 与被省略号截断的文本会自动弹同款浮层。需要自定义提示内容（与页面显示的文本不同）、
 * 关闭复制按钮、或指定方位时才显式用本组件。两者不会重复弹：Element Plus 会给触发元素加上
 * el-tooltip__trigger 类名，全局引擎见到该类名就跳过。
 * - 浅色浅蓝浮层、带箭头、宽高不超屏幕（样式见 styles/main.css 的 .el-popper.el-tooltip.cs-tip）；
 * - 内容必须包一层 .cs-tip-body：限高与滚动条只能落在这个内容层上，
 *   若直接给浮层写 overflow，浮层外侧的箭头会被计入滚动区域而凭空多一根滚动条；
 * - 与触发元素零间隙（offset=0）：留空隙时鼠标无法连续移到浮层上，
 *   中途会先触发上方元素自己的提示，导致选不中内容、点不到复制按钮；
 * - 时间逻辑向浏览器自带提示对齐：停留 0.5s 才显示（showAfter）、显示 5s 后自动消失（autoClose）；
 *   鼠标移进浮层会重新触发 onOpen，自动消失的倒计时跟着重算，因此还能正常选中内容、点复制按钮；
 *   时间参数与 App.vue 的表格全局配置必须保持一致；
 * - 内容型提示右上角自动带复制按钮（由 common/tipCopy.ts 注入），鼠标可移进浮层点击；
 * - 纯操作提示（刷新/全屏这类按钮说明）传 :copyable="false"，不加复制按钮；
 * - 处于浏览器全屏容器内时传 :teleported="false"，否则浮层挂到 body 上在全屏里看不见。
 * 表格单元格的溢出提示由 Element Plus 表格内部创建，走 App.vue 的全局 table 配置，
 * popperClass 与此处一致，因此外观与复制行为完全相同。
 */
import { computed } from 'vue'

/** 项目内会用到的浮层方位 */
type TipPlacement =
  | 'top' | 'top-start' | 'top-end'
  | 'bottom' | 'bottom-start' | 'bottom-end'
  | 'left' | 'right'

const props = withDefaults(
  defineProps<{
    /** 提示文本；富文本用 #content 插槽 */
    content?: string
    placement?: TipPlacement
    /** 是否在浮层右上角显示复制按钮：内容型提示用默认值，按钮说明类提示传 false */
    copyable?: boolean
    /** 禁用提示（如内容为空时） */
    disabled?: boolean
    /** 是否挂到 body：在浏览器全屏容器内需传 false，否则浮层在全屏里看不见 */
    teleported?: boolean
    /** 追加的浮层类名（统一类名 cs-tip 由组件自己加） */
    popperClass?: string
  }>(),
  { placement: 'top', copyable: true, disabled: false, teleported: true },
)

// cs-tip 决定外观，cs-tip-copyable 决定是否注入复制按钮（见 common/tipCopy.ts）
const kls = computed(() =>
  ['cs-tip', props.copyable ? 'cs-tip-copyable' : '', props.popperClass || '']
    .filter(Boolean)
    .join(' '),
)
</script>

<template>
  <el-tooltip
    :content="content"
    :placement="placement"
    :disabled="disabled"
    :teleported="teleported"
    :popper-class="kls"
    effect="light"
    :offset="0"
    :show-after="500"
    :auto-close="5000"
    :enterable="true"
    :hide-after="300"
  >
    <slot />
    <!-- 内容统一包一层滚动容器；未传 #content 插槽时回退到 content 属性的文本 -->
    <template #content>
      <div class="cs-tip-body"><slot name="content">{{ content }}</slot></div>
    </template>
  </el-tooltip>
</template>
