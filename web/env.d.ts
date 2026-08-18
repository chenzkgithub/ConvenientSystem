/// <reference types="vite/client" />

declare module 'vue' {
  interface ComponentCustomProperties {
    /** 按钮级权限检查：模板中 v-if="$has('permission-code')" */
    $has: (code: string) => boolean
  }
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}

interface ImportMetaEnv {
  /** 远程接口基址：为空走相对路径（exe 内嵌），非空直接请求远程服务器（独立浏览器部署） */
  readonly VITE_API_BASE?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
