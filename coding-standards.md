# ConvenientSystem 开发规范

> 做需求前必读。所有新增/修改代码必须遵守以下规范。

---

## 一、开发流程

1. **先方案后动工**：每次开发新功能时，先给出完整方案（数据模型、API、页面设计等），等用户确认后再写代码。
2. **验证责任归属**：完成后不自动执行前端构建（npm run build）和后端编译重启，由用户自行验证并反馈。默认仅做代码修改与静态检查（vue-tsc）。
3. **严禁在工程目录生成任何临时文件**：不在项目内生成任何非必需附带文件，包括但不限于：.md 说明文档、总结报告、方案文档、示例代码、临时脚本（.ps1/.cmd/.sh）、部署脚本、同步脚本、备份文件等。所有说明与方案一律直接在对话中回复。Agent 自用的临时验证程序/脚本必须写入系统 `%TEMP%` 目录（用 shell 命令创建，`Write` 工具无法写工程外），用完即删，绝对不放在工程目录内（含 `bin/`、`obj/` 等构建目录）。发布产物统一使用约定目录。
4. **SQL 变更自动执行**：修改 db/init.sql 或其他 SQL 脚本后，自动在本地 LocalDB 执行变更，不需用户额外确认。

---

## 二、前端规范

### 2.1 通用组件优先

新增页面/功能前，先在 `web/src/common/` 目录与既有同类页面中查找可复用的封装。只有确认封装无法满足时才写自定义实现，并优先考虑把能力补充进现有封装组件而非另起一份。已有原生实现在改动相关代码时应顺带迁移到统一封装。

### 2.2 列表页统一使用 CommonDataTable

所有带分页、筛选、行操作的后台列表页，统一使用 `web/src/common/components/CommonDataTable.vue`。该组件封装了筛选区/工具栏布局、列渲染、分页、加载状态、keep-alive 切回自动刷新等。

### 2.3 筛选区与工具栏插槽职责分离

`#filters` 插槽（左侧）放筛选输入控件（input、select、date-picker等）及「查询 / 重置」按钮。查询/重置属于筛选区，不放 toolbar。

`#toolbar` 插槽（右侧）放功能按钮（新增、刷新、清空、导出等）。toolbar 通过 `justify-content: space-between` 自动右对齐，禁止在组件外单独写工具栏 div（如 .btn-bar）。

推荐使用组件内置的 `searchable` 属性自动渲染查询/重置按钮，而非在 `#filters` 里手写 `<el-button>`。

- 独立列表页（页面容器无内边距）使用默认非 compact 模式，内边距 12px
- compact 模式仅用于自身已有内边距的容器（弹窗 body、嵌入面板等）

### 2.4 列表页间距统一规范

所有列表页根容器统一使用 `display: flex; flex-direction: column; height: 100%; overflow: hidden;`，**不加 padding**。页面的上下左右间距由 CommonDataTable 内部统一管理（header 12px、body 0 12px），禁止在根容器额外加 padding 叠加导致间距过大。新增列表页必须遵循此模式，现有页面在改动时顺带修正。

### 2.5 表格内部滚动禁止页面级滚动条

列表数据多时由表格自身出现纵向滚动条，页面（含弹窗/抽屉）不允许出现整体滚动条。实现方式：列表页根容器 `height:100% + flex 纵向布局 + overflow:hidden`，CommonDataTable 默认表格高度 100% 撑满容器内部滚动。嵌入弹窗的表格需显式传 `max-height`（如 52vh）。

### 2.5b 弹窗滚动条必须在弹窗内部

弹窗内容过高时，弹窗自身出现纵向滚动条，外部页面不允许出现整体滚动条。`.el-dialog` 设 `max-height: calc(100vh - 100px)`，`.el-dialog__body` 设 `overflow: auto`。

### 2.6 悬浮提示统一使用 CommonTooltip

全站悬浮提示统一走一套机制，禁止直接写 `el-tooltip`：

- 全局自动提示：`common/globalTip.ts` 自动解析 title 和文本截断
- 显式组件：`@/common/components/CommonTooltip.vue`，用于自定义提示内容
- 表格溢出提示：App.vue 的 `el-config-provider :table` 统一配置
- 三处时间参数一致：`showAfter:500`、`autoClose:5000`、`enterable:true`、`hideAfter:300`、`offset:0`

### 2.7 API 请求统一使用内置封装

所有后端 API 调用必须使用 `@/api/request.ts` 的 `httpGet`/`httpPost`/`httpPut`/`httpDelete`，禁止使用原始 `fetch()` 或 `axios`。封装自动处理 JWT 认证、全局 loading、401 自动登出、错误信息提取。

### 2.8 弹窗统一使用 CommonDialog

所有弹窗必须使用 `web/src/common/components/CommonDialog.vue`，禁止直接写 `el-dialog`。CommonDialog 基于 el-dialog 封装，内置全屏/还原图标按钮、默认 `close-on-click-modal=false`（防误关）、默认 `append-to-body=true`、默认 `draggable=true`。拖动/拉伸由全局 `dialogFlex.ts` 自动增强。新增弹窗时只需传 `v-model`、`title`、`width` 和内容插槽即可。

### 2.9 中文拼音搜索统一使用 common/pinyin.ts

中文内容的拼音搜索统一使用 `web/src/common/pinyin.ts`，不要在页面里各自调用 `pinyin-pro`。导出 `pinyinMatchIndex(text, keyword)` 与 `pinyinMatch(text, keyword)`，匹配顺序为原文 → 拼音首字母缩写 → 无声调全拼。

### 2.10 视觉设计规范（Linear 极简风）

全局视觉风格基于 `web/src/styles/main.css` 的 CSS 变量体系，所有页面必须遵循以下约定：

- **主色**：亮蓝 `#3b82f6`，通过 `--brand` 变量引用。禁止在 scoped 样式中硬编码品牌色值（如 `#2fa98f`、`#3b82f6`），统一用 `var(--brand)` / `var(--brand-dark)` / `var(--brand-gradient)` 等变量
- **侧边栏**：浅色主题（`--sidebar-bg: #fafafa`），禁止恢复深色侧栏。菜单项激活态用 `--sidebar-active-bg`（blue-50 浅蓝底）+ `--sidebar-active-text`（blue-600 文字）
- **字体**：纯 system-ui 字体栈（`-apple-system, 'Segoe UI Variable', system-ui, 'Microsoft YaHei UI', sans-serif`），不引入外网字体（内网部署约束）
- **圆角**：统一使用 `--radius`（12px）变量，小元素用 `--radius-sm`（8px），大容器用 `--radius-lg`（16px）。禁止硬编码 `border-radius` 数值
- **阴影**：去重阴影、用细边框分隔。容器分隔优先用 `1px solid var(--border)`，仅在需要层次感时用 `--shadow-sm` / `--shadow-md`
- **导航模式**：默认面包屑导航（`showTabsBar = false`），老用户可切换回多标签栏。页面不需要关心当前模式，仅监听 `@load` 事件即可
- **图标色系**：首页 KPI 卡片和功能入口的图标渐变统一用蓝色系（blue / sky / cyan / indigo），禁止出现旧青绿色（teal `#2fbfa0` / `#17a2b8`）

---

## 三、后端规范

### 3.1 系统配置统一走 SysConfig 表

- **DB 优先**：所有系统配置项存储于 SysConfig 表，appsettings.json 仅保留启动必需项（ConnectionStrings、ServicePort）
- **统一服务封装**：Service 层通过 `ISysConfigService.GetValue()` 读取配置；Shared 层直接用 keyed IFreeSql 查询 SysConfig 表，禁止直接依赖 IConfiguration
- **英文键展示**：前端 SysConfigView.vue 在中文显示名下方展示对应的英文配置键
- **JWT 密钥一致性**：ServicesExtent（验证方）和 LoginService（签发方）必须从同一来源（SysConfig 表）读取 Jwt.Key

### 3.2 FreeSql decimal 属性必须显式标注精度

实体中未标注特性的 decimal 属性默认按 `decimal(10,2)` 生成 SqlParameter，大金额值会报"参数值超出范围"。金额、销量、奖池等大数值 decimal 属性必须显式标注 `[Column(Precision = 18, Scale = 2)]`，与建表 SQL 的 `DECIMAL(18,2)` 保持一致。

---

## 四、数据库规范

### 4.1 SQL 脚本表与字段必须添加中文注释

在 db/init.sql 中新建表或新增字段时，必须同时维护中文注释：表需写 `EXEC dbo.usp_AddTableComment` 语句，列需用 `EXEC dbo.usp_AddColumnComment` 或 `-- 中文说明` 形式注释。

### 4.2 迁移脚本与 init.sql 必须同步维护

编写迁移脚本（db/migrations/NNN_xxx.sql）时，必须同步维护 db/init.sql 初始化脚本。init.sql 是全新数据库的建库入口，迁移脚本面向已有库的增量变更，两者必须保持一致。

### 4.3 SQL 变更自动执行到本地库

连接信息：`Server=(localdb)\MSSQLLocalDB, Database=ConvenientSystem, Integrated Security=True`

---

## 五、领域特定规范

- **选号记录**：必须包含期号与开奖日期字段
- **开奖结果邮件**：必须使用独立定时汇总任务触发
- **邮件 HTML 表格**：单元格 `text-align:center` 必须显式声明（邮件客户端继承失效）
- **外部页面**：访问链接旁必须有“打开”和“复制”图标按钮，只显示图标不显示文字（用 title 属性提供悬浮提示）；新增外部页面时路由路径根据所选组件自动生成（组件名 PascalCase 转 kebab-case，统一加 out- 前缀）
- **考勤管理模块**：所有需求排除考勤管理模块及其数据库

---

## 六、经验记忆

> 本节集中存放项目知识、架构约定、技能经验、踩坑教训、技术决策与任务总结。新增记忆追加到对应小节；已沉淀为规范的要点同时见正文相关章节，此处作为记忆索引保留。

### 6.1 项目知识

**项目概述**
- 定位：一站式内部工具管理平台，覆盖数据查询、定时任务、监控告警、消息通知、彩票分析等业务场景
- 架构：前后端分离（ASP.NET Core + Vue 3），桌面端通过 WinForms + WebView2 提供原生窗口体验
- 核心模块：系统管理（用户/角色/权限/菜单/配置）、日志（审计/错误/实时）、运维监控（大盘/定时任务/主机/网站）、开发工具（SQL 查询/命名转换/Python 知识库）、彩票分析（走势/历史/智能推荐）、消息通知（短信/邮件/群机器人）、效率工具（命令面板/快捷键/暗黑模式）

**技术栈**
- 后端：ASP.NET Core 10（.NET 10），ORM 用 FreeSql（SQL Server），定时任务用 Hangfire 1.8（SqlServer 持久化）
- 前端：Vue 3 + TypeScript + Vite 6，UI 用 Element Plus 2.9，图表 ECharts 5，代码编辑器 Monaco Editor，状态管理 Pinia
- 桌面端：WinForms + WebView2
- 数据库：SQL Server LocalDB（开发）/ SQL Server Express（生产）

**开发环境搭建与启动**
- 运行环境：.NET 10 SDK、Node.js 18+、SQL Server LocalDB 或 SQL Server 实例
- 启动步骤：1) 初始化数据库 `sqllocaldb start MSSQLLocalDB` 后 `sqlcmd -S (localdb)\MSSQLLocalDB -E -i db\init.sql`；2) 安装前端依赖 `cd web && npm install`；3) 分别运行 `dev-backend.cmd` 和 `dev-web.cmd`
- 默认账号：admin / admin

**构建与发布配置**
- `dev-backend.cmd`：启动后端开发服务（端口 51943）
- `dev-web.cmd`：启动前端热更新服务（端口 5173，代理到 51943）
- `publish.cmd`：一键发布（前端构建 → API 打包到 api/ → 桌面端打包到 exe/）
- `start.cmd`：启动已发布的程序
- 发布产物：`api/`（单文件自包含）、`exe/`（WinForms + WebView2）

### 6.2 架构约定

**FreeSql 动态排序扩展点**
- 新增动态排序能力需改三处：1) 新建工具类 `FreeSqlSortExtensions.cs`，提供安全的 `OrderByDynamic<T>` 方法（按实体校验属性名，防 SQL 注入）；2) 各分页服务查询链在分页前调用 `OrderByDynamic`；3) 对应 Controller 接收前端的 `sortField`/`sortOrder` 并传入 Service

**视图管理模块导航菜单位置**
- 视图管理模块必须排在菜单管理之前，以体现其定义页面及权限点的基础性作用

### 6.3 规范要点（记忆补充）

> 以下要点同时在正文相关章节体现，此处作为记忆索引保留。

**CommonDataTable 操作列必须支持列配置**
- 操作列（Actions Column）必须在列配置面板中支持显示/隐藏控制，供用户自定义表格列可见性

**弹窗必须使用 CommonDialog**
- 所有弹窗用 `web/src/common/components/CommonDialog.vue`，禁止直接写 `el-dialog`
- CommonDialog 内置三个标准头按钮：折叠吸底（Minus）、全屏/还原（FullScreen/Aim）、关闭（Close）；自动提供拖动/拉伸 + close-on-click-modal=false
- 违规典型表现：用户发现弹窗缺少“那三个按钮”（折叠/全屏/关闭）

**UI 偏好持久化到 UserConfig 表**
- UI 偏好（侧边栏折叠、标签记忆、导航模式、主题模式）必须存入 UserConfig 数据库表，每次登录从数据库读取，不用 localStorage

**方案 B：页面与按钮权限独立授权模型**
- 采用独立授权模型：页面访问（菜单导航）与按钮级权限分别授权。授予页面访问不会自动授予其按钮权限，每个按钮权限需单独分配

### 6.4 技能经验

**批量注入 props 到 Vue 组件的标准化流程**
- 目标：给可复用 Vue 组件的所有实例批量注入相同 props（如 show-refresh、show-column-toggle、table-key），保证一致性
- 步骤：
  1. grep 定位所有包含目标组件名的文件（先拿完整使用清单，避免漏实例）
  2. grep 在每个文件中定位组件标签起始行（精确行定位，避免注入到注释或无关上下文）
  3. SearchReplace 在各文件起始标签处注入新 props（直接字符串替换，原子性）
  4. 阶段校验：注入实例数是否等于原始清单数；不一致则扩大搜索范围，捕捉动态导入或别名引用
- 注意：逐个手改易不一致且漏实例，必须用 grep + SearchReplace；仅按组件名搜不核对起始标签，有注入到注释/错行的风险

### 6.5 踩坑经验

**前端登录流程执行顺序导致 401**
- 现象：登录后立即被 401 踢出
- 根因：`login()` 中 `loadUIPrefs()` 在 `persist()` 之前调用，API 用的是 localStorage 里的空 token 而非新 token
- 修复：把 `persist()` 移到设置 `token.value` 之后立即执行，确保新 token 写入 localStorage 后再发后续已认证请求
- 适用：实现/修改前端登录流程时；不适用后端鉴权逻辑

**SQL Server 重复 ORDER BY 错误**
- 现象：SQL Server 报 “A column has been specified more than once”
- 根因：传了动态排序字段时，服务方法仍叠加了硬编码 `.OrderByDescending(l => l.CreateTime)`，导致 ORDER BY 出现两个相同列
- 修复：所有分页服务方法改为仅当 `sortField` 为空时才应用默认排序（条件分支）
- 适用：SQL Server 分页查询实现动态排序时；不适用允许 ORDER BY 重复列的数据库

**sqlcmd 读取 UTF-8 SQL 文件需指定 -f 65001 编码**
- 现象：含中文的 UTF-8（无 BOM）SQL 文件被 sqlcmd 执行后中文乱码、批处理静默失败（返回 “(0 rows affected)” 但不报错）
- 根因：sqlcmd 默认按系统 OEM 代码页（中文 Windows 为 GBK/936）读取 `-i` 文件，`N'中文'` 被误读为乱码字节，SQL 解析失败、整个 batch 被静默跳过
- 修复：执行含中文的 UTF-8 SQL 文件必须加 `-f 65001`：`sqlcmd -S "(localdb)\MSSQLLocalDB" -d dbname -i "file.sql" -f 65001 -W`；或将文件存为 UTF-8 with BOM / GBK
- 适用：执行含中文的 UTF-8 迁移脚本；不适用纯 ASCII 文件或带 BOM 文件

### 6.6 技术决策

**UI 偏好持久化策略：数据库优先 + localStorage 兜底**
- 结论：UI 偏好以数据库（UserConfig 表）为唯一事实源，localStorage 仅作未登录/离线场景兜底
- 取舍：数据库保证跨设备一致与集中管理；localStorage 提供即时响应与无网络可用性。权衡一致性 vs 可用性
- 否决方案：纯 localStorage（跨设备不一致，弃）；纯数据库无兜底（断网或登录前体验差，弃）
- 适用条件：UI 偏好需跨设备/会话一致时成立；若转向完全离线优先架构需重新评估

### 6.7 任务总结

**登录会话竞态修复**
- 需求：修复登录成功后立即被 401 踢出的 bug
- 根因：登录流 `token.value = data.token` → `await loadUIPrefs()` → `persist()`，loadUIPrefs 用的是 localStorage 空 token
- 修复：把 `persist()` 提前到设置 `token.value` 后立即执行
- 关键文件：`web/src/common/stores/auth.ts`、`web/src/api/request.ts`
- 教训：多会话场景下，错误处理里必须比对当前 token 与请求 token，过滤过期响应

**CommonDataTable 排序功能（后端动态排序支持）**
- 需求：给 CommonDataTable 加排序能力并应用到全系统列表页（考勤页除外）
- 实施：前端扩展 `useDataTable` 管理 `sortField`/`sortOrder` 并在 `buildParams()` 上送；后端建 `FreeSqlSortExtensions.cs` 的安全 `OrderByDynamic<T>`；更新 10 个分页服务与 Controller；8 个分页视图加 `sortable:'custom'`，SmsTemplateView 加 `sortable:true`；修复 SQL Server 重复 ORDER BY
- 关键文件：`useDataTable.ts`、`FreeSqlSortExtensions.cs`、各 Service/Controller、ErrorLogView/AuditLogView 等 21+ 页面
- 教训：动态排序字段存在时不要再叠加硬编码默认排序，否则触发 SQL Server 重复列错误

**CommonDataTable 刷新按钮标准化与列切换配置**
- 需求：标准化全项目 CommonDataTable 刷新按钮用法、修正列切换配置
- 实施：给 15 个缺 @load 的页面补 `@load="loadData"/"load"`；清除 11 处重复 @load；移除 UserOnlineView 工具栏多余手写刷新按钮
- 注意：操作列硬编码不进列配置面板，设计上应常驻可见

**CommonDataTable 标准化、Hangfire 日志重构及登录会话竞态修复**
- 需求：1) 标准化 CommonDataTable 刷新按钮与列切换；2) Hangfire 任务日志弹窗由 el-table+expand 换成 CommonDataTable；3) 修复重登后被踢的会话竞态
- 实施：CommonDataTable 标准化（补 @load、去重、去多余按钮）；Hangfire 日志改用 CommonDataTable + CommonDialog 详情弹窗；登录竞态在 request.ts 的 401 处理里加 token 比对过滤过期请求，登录成功后调 `resetUnauthorizedHandled()` 重置标志
- 关键文件：`CommonDataTable.vue`、`HangfireJobsView.vue`、`request.ts`、`auth.ts` 等
- 教训：多会话场景下错误处理必须比对当前 token 与请求 token，过滤过期响应

**视图管理全量注册与按钮级权限控制**
- 需求：把所有现有前端页面注册到视图管理系统并加按钮级权限控制
- 实施：迁移脚本 017_add_all_views.sql 插入 30 条 SysView、56 条 SysViewPermission（IF NOT EXISTS）；init.sql 同步种子（37 视图、68 权限）；main.ts 注册 `$has` 全局属性 + env.d.ts 类型声明；重构 22 个 Vue 文件把 `v-if="has(...)"` 换成 `v-if="$has(...)"`
- 关键文件：`db/migrations/017_add_all_views.sql`、`init.sql`、`main.ts`、`env.d.ts`、`usePermission.ts`、各视图
- 教训：sqlcmd 执行含中文的 UTF-8 SQL 必须加 `-f 65001`，否则 batch 静默失败

**Hangfire 任务日志表创建与视图管理搜索功能**
- 需求：1) 修复 Hangfire 任务报 'Invalid object name dbo.JobExecutionLog'；2) ViewManageView 加视图搜索；3) MainLayout 加标签记忆开关
- 实施：执行已有的 017_add_job_execution_log.sql（sqlcmd -f 65001）；ViewManageView 左侧列表加 el-input 搜索 + searchKey + filteredViews 客户端过滤；tabs.ts 加 REMEMBER_KEY 开关，loadPersisted/persist 据此跳过，MainLayout 头部加图标按钮
- 关键文件：`017_add_job_execution_log.sql`、`ViewManageView.vue`、`tabs.ts`、`MainLayout.vue`
- 教训：迁移脚本存在不代表已执行，需确认表是否实际建好

**Hangfire 任务日志显示 bug：同类定时任务 JobName 区分**
- 需求：修复点第一个定时任务“触发”却把日志显示到第二个任务下
- 根因：两个定时任务都用 `LotteryDrawCrawlJob` 类但 recurring job ID 不同；`JobExecutionLog.JobName` 存的是类名/硬编码名而非 recurring job ID，导致日志混在一起
- 修复：给 `CrawlAsync` 加可选 `logJobName` 参数；`BackfillRecentAsync` 调用时传 `“{彩种}奖级明细补拉”` 作为 logJobName
- 关键文件：`ConvenientSystem.Shared/Jobs/LotteryDrawCrawlJob.cs`
- 教训：多个定时任务共用同一类/方法时，`ExecuteWithLog` 必须为每个 recurring job ID 传唯一的 jobName，日志才能正确过滤

**UI 偏好持久化从 localStorage 迁移到 UserConfig 表**
- 需求：把 UI 偏好（侧边栏折叠、标签记忆、导航模式、主题模式）从 localStorage 迁到 UserConfig 表，实现跨设备/浏览器一致
- 实施：后端在 UserConfigService.cs 的 ConfigMetadata 加 4 个 UI 配置键（UI.SidebarCollapsed/UI.RememberTabs/UI.NavMode/UI.ThemeMode）；加 `GetUIPrefs()` 与 `GET /api/userconfig/ui-prefs`；前端建 `useUserPrefs.ts` composable（DB 优先 + localStorage 兜底 + 防抖保存）；auth 登录后加载 UI 偏好；tabs.ts/theme.ts/MainLayout.vue 改用 `useUserPrefs()` 替代直接 localStorage；加 `applyServerPrefs()` 同步
- 关键文件：`UserConfigService.cs`、`IUserConfigService.cs`、`UserConfigController.cs`、`useUserPrefs.ts`、`auth.ts`、`tabs.ts`、`theme.ts`、`MainLayout.vue`
- 结果：4 个偏好跨浏览器/设备持久化，个人配置页自动显示“界面偏好”区
