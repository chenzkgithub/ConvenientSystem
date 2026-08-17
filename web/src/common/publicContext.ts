/**
 * 页面上下文判定（全站唯一判定源）
 *
 * 两个正交的 URL 标记，禁止在其它文件里自行正则嗅探，一律从本模块引入：
 * - standalone=1：窗口呈现模式。只渲染目标页面本身，跳过主框架与登录门禁，
 *   由托盘菜单/悬浮按钮打开内部页面时使用，与认证无关（照常携带 JWT）。
 * - public=1：免登录公开上下文。外部分享链接使用，请求不携带 JWT，
 *   401 静默忽略，绝不触碰主窗口的登录态。
 *
 * 两个常量在模块首次加载时（路由初始化之前）就地求值并固化：
 * 后续 hash 变化不会改变已建立的上下文，避免运行期行为漂移与判定竞态。
 *
 * 安全边界不在前端：public=1 仅决定客户端是否发送 JWT，
 * 接口能否匿名访问始终由后端 [AllowAnonymous] 决定，手动给内部页面拼 public=1 取不到任何数据。
 */
const initialHash = typeof window !== 'undefined' ? window.location.hash : ''

/** 免登录公开上下文（外部分享链接） */
export const IS_PUBLIC_CONTEXT = /[?&]public=1/.test(initialHash)

/** 纯净窗口模式（无主框架、无菜单栏） */
export const IS_STANDALONE = /[?&]standalone=1/.test(initialHash)

/** 仅渲染目标页面本身：纯净窗口与公开页面都不显示主框架 */
export const IS_BARE_WINDOW = IS_STANDALONE || IS_PUBLIC_CONTEXT
