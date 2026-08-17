import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig(({ command }) => ({
  // dev 模式用绝对路径，build 时用相对路径（保证 WebView2 内加载正常）
  base: command === 'serve' ? '/' : './',
  plugins: [
    vue(),
    // Element Plus 按需自动引入 API 与组件
    AutoImport({ resolvers: [ElementPlusResolver()] }),
    Components({ resolvers: [ElementPlusResolver()] }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    // 直接产出到桌面客户端项目的 wwwroot，由其 Kestrel 作为静态站点提供（exe 内嵌 WebView2 与浏览器共用）
    outDir: fileURLToPath(new URL('../ConvenientSystem.Desktop/wwwroot', import.meta.url)),
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    // 开发热更新：接口代理到 dotnet 后端（固定端口 51943，见 ConvenientSystem.Api/Program.cs）。
    // 注意 51942 是桌面客户端静态站点的端口，不是接口服务的端口，勿混用。
    // 后端统一 area 路由 api/{area}/{controller}/{action}，故一条 /api 规则即可覆盖全部接口。
    proxy: {
      '/api': 'http://localhost:51943',
      '/hangfire': 'http://localhost:51943',
    },
  },
}))
