# ConvenientSystem 开发规范

> 做需求前必读。所有新增/修改代码必须遵守以下规范。

---

## 一、开发流程

1. **先方案后动工**：每次开发新功能时，先给出完整方案（数据模型、API、页面设计等），等用户确认后再写代码。
2. **验证责任归属**：完成后不自动执行前端构建（npm run build）和后端编译重启，由用户自行验证并反馈。默认仅做代码修改与静态检查（vue-tsc）。
3. **禁止遗留临时文件**：不在项目内生成 .md 说明文档、临时脚本、备份文件等。临时脚本用完即删。发布产物统一使用约定目录。
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
- **外部页面**：访问链接旁必须有"打开"和"复制"图标按钮，只显示图标不显示文字（用 title 属性提供悬浮提示）；新增外部页面时路由路径根据所选组件自动生成（组件名 PascalCase 转 kebab-case，统一加 out- 前缀）
- **考勤管理模块**：所有需求排除考勤管理模块及其数据库
