import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import App from './App.vue'
import './styles/main.css'
// 函数式调用（ElMessage / ElMessageBox）的样式不会随组件自动引入，需手动导入
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
// v-loading 指令需手动注册（unplugin 只自动处理组件，不处理指令）
import { ElLoading, ElMessage } from 'element-plus'
import 'element-plus/es/components/loading/style/css'
import { ApiError } from '@/api/request'
import { useAuthStore } from '@/common/stores/auth'
import { installDialogFlex } from '@/common/dialogFlex'
import { installTipCopy } from '@/common/tipCopy'

// Element Plus 组件与样式由 unplugin-vue-components / unplugin-auto-import 按需引入，
// 此处仅需引入暗色变量以外的基础重置由 main.css 提供。
const app = createApp(App)
app.directive('loading', ElLoading.directive)
app.use(createPinia())

// 全局权限检查：模板中可直接用 v-if="$has('permission-code')"，无需在各页面 import
app.config.globalProperties.$has = (code: string): boolean =>
  useAuthStore().menuCodes.includes(code)
app.use(router)

// 全局错误统一弹提示（不再跳错误页）：grouping 合并相同内容提示，避免连环错误刷屏
function showErrorToast(title: string, message: string) {
  ElMessage.error({ message: `${title}：${message}`, grouping: true, duration: 6000 })
}

app.config.errorHandler = (err, _vm, info) => {
  console.error('[Vue global error]', err, info)
  showErrorToast('页面渲染出错', `${(err as Error).message || String(err)}（${info}）`)
}

app.mount('#app')

// 全局弹窗增强：所有 ElDialog 支持标题栏拖动与右下角拉伸
installDialogFlex()

// 全局提示增强：统一样式的悬浮提示（表格溢出提示与 CommonTooltip）右上角补一个复制按钮
installTipCopy()

// 全局 JS 运行时错误与未处理的 Promise 异常：统一弹提示，不阻断当前页面
window.onerror = (message, _source, _lineno, _colno, err) => {
  // ResizeObserver loop 警告是浏览器良性通知（虚拟表格/弹窗尺寸变化时触发），不是真实错误，忽略
  if (String(message || '').includes('ResizeObserver loop')) return true
  console.error('[window.onerror]', message, err)
  showErrorToast('运行发生错误', String(message || '未知脚本错误'))
  return true
}

window.onunhandledrejection = (event) => {
  console.error('[unhandledrejection]', event.reason)
  // ApiError 已由 request.ts 弹出错误提示，不再重复提示
  if (event.reason instanceof ApiError) return
  const reason = event.reason
  const msg = reason instanceof Error ? reason.message : String(reason || '未知异步错误')
  showErrorToast('异步操作发生错误', msg)
}
