// vue 组件实例属性类型扩展（模块文件：有顶层 export，declare module 'vue' 才是模块扩展而非环境声明）
// 若放在全局脚本（如 env.d.ts）里，会遮蔽 node_modules 的真实 vue 类型，引发全项目 TS2305
export {}

declare module 'vue' {
  interface ComponentCustomProperties {
    /** 按钮级权限检查：模板中 v-if="$has('permission-code')" */
    $has: (code: string) => boolean
  }
}
