/// <reference types="vite/client" />

// 注意：vue 的 ComponentCustomProperties 扩展（$has 等）放在模块文件 src/types/vue.d.ts 里。
// 本文件是全局脚本（无顶层 import/export），此处的 declare module 'vue' 会变成环境模块声明，
// 遮蔽 node_modules 里真实的 vue 类型，导致所有 .vue 报 "Module 'vue' has no exported member 'ref'"。

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
