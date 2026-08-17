# ConvenientSystem — 便捷综合管理系统

一站式内部工具管理平台，覆盖数据查询、定时任务、监控告警、消息通知、彩票分析等业务场景。  
采用 **ASP.NET Core + Vue 3** 前后端分离架构，桌面端通过 **WinForms + WebView2** 提供原生窗口体验。

---

## 技术栈

| 层面 | 技术 |
|------|------|
| 后端框架 | ASP.NET Core 10 (.NET 10) |
| ORM | FreeSql（SQL Server） |
| 定时任务 | Hangfire 1.8（SqlServer 持久化） |
| 前端框架 | Vue 3 + TypeScript + Vite 6 |
| UI 组件库 | Element Plus 2.9 |
| 图表 | ECharts 5 |
| 代码编辑器 | Monaco Editor |
| 状态管理 | Pinia |
| 桌面端 | WinForms + WebView2 |
| 数据库 | SQL Server LocalDB（开发）/ SQL Server Express（生产） |

---

## 项目结构

```
ConvenientSystem/
├── ConvenientSystem.Api/          # 接口服务（REST API + Hangfire 面板）
│   ├── Auth/                      # JWT 认证 & 权限鉴权
│   ├── Controllers/               # 按业务模块分组的控制器
│   ├── Middleware/                 # 审计中间件、用户状态校验、内存日志缓冲
│   ├── Services/                  # 租户/订阅/功能开关
│   └── Program.cs                 # 启动入口
├── ConvenientSystem.Service/      # 业务逻辑层
│   ├── Common/                    # 通用服务（用户、角色、菜单、配置、日志、SQL 查询等）
│   ├── Email/                     # 邮件配置与发送任务
│   ├── Sms/                       # 短信配置、模板与发送任务
│   └── Interface/                 # 服务接口定义
├── ConvenientSystem.Shared/       # 共享层（实体、DTO、基础设施）
│   ├── Common/                    # 审计、安全、过滤器、Webhook 等基础设施
│   ├── Entity/                    # FreeSql 实体定义
│   ├── Jobs/                      # Hangfire 定时任务
│   └── Model/                     # DTO 与请求/响应模型
├── ConvenientSystem.Desktop/      # 桌面客户端（WinForms + WebView2 + 反向代理）
├── web/                           # Vue 3 前端工程
│   └── src/
│       ├── common/                # 通用模块（组件、视图、Store、工具）
│       ├── email/                 # 邮件模块视图
│       ├── notify/                # 群机器人通知模块
│       ├── sms/                   # 短信模块视图
│       └── yunhan/                # 云瀚考勤模块
├── db/
│   ├── init.sql                   # 数据库初始化脚本（幂等，唯一维护入口）
│   └── migrations/                # 增量迁移脚本
├── dev-backend.cmd                # 启动后端开发服务（端口 51943）
├── dev-web.cmd                    # 启动前端热更新服务（端口 5173，代理到 51943）
├── publish.cmd                    # 发布打包（API → api/，桌面端 → exe/）
└── start.cmd                      # 启动已发布的程序
```

---

## 功能模块

### 系统管理
- **用户管理** — 账号增删改查、启用/禁用
- **角色管理** — 角色 CRUD、菜单权限分配、数据权限范围
- **权限设置** — 可视化权限树，按角色分配菜单访问权
- **在线用户** — 实时在线用户列表，支持强制下线
- **通知管理** — 站内通知发布与推送
- **系统配置** — 键值对配置管理（DB 优先，支持多种输入类型）
- **外部页面** — 免登录公开页面配置（`?public=1` 访问）
- **个人配置** — 用户个人偏好设置
- **菜单管理** — 可视化菜单树编辑、拖拽排序

### 日志
- **审计日志** — 用户写操作记录（POST/PUT/DELETE），含操作人、参数摘要、耗时
- **错误日志** — 未处理异常归档，含堆栈、请求路径、异常类型
- **实时日志** — 内存日志流，5 秒自动刷新，终端风格查看器

### 运维监控
- **系统大盘** — 服务器信息、进程资源（内存/线程/句柄）、磁盘空间、Hangfire 统计
- **定时任务** — Hangfire 周期任务列表，支持手动触发
- **主机监控** — CPU/内存/磁盘/网络多维度 Grafana 风格 Dashboard
- **网站监控** — HTTP 端点可用性检测与响应时间监控

### 开发工具
- **SQL 查询工具** — Monaco Editor 代码编辑器、多数据源、收藏管理、快捷输入
- **命名转换** — 驼峰/蛇形/帕斯卡等多种命名风格互转
- **Python 知识库** — Markdown 知识库管理与查看

### 彩票分析
- **开奖走势** — 多彩种走势图（双色球、大乐透、排列五、福彩3D）
- **历史记录** — 选号记录管理，含期号与开奖日期
- **开奖汇总** — 开奖结果汇总与数据导出
- **智能分析** — 基于规则的号码分析与推荐

### 消息通知
- **短信管理** — 多供应商（阿里云/互亿无线）、模板管理、发送日志
- **邮件管理** — SMTP 配置、邮件任务调度
- **群机器人** — 企业微信/钉钉/飞书 Webhook 配置与发送日志

### 效率工具
- **命令面板**（Ctrl+K）— 快速搜索并跳转到任意菜单页面
- **快捷键帮助**（? / Ctrl+/）— 快捷键速查浮层
- **暗黑模式** — 一键切换，支持跟随系统
- **最近访问** — 侧边栏快速回访历史页面

---

## 开发环境搭建

### 前置条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/)
- SQL Server LocalDB（Visual Studio 自带）或 SQL Server 实例

### 1. 初始化数据库

```powershell
# 启动 LocalDB
sqllocaldb start MSSQLLocalDB

# 执行初始化脚本（幂等，可重复执行）
sqlcmd -S (localdb)\MSSQLLocalDB -E -i db\init.sql -f 65001
```

> 如果使用完整版 SQL Server，将 `-S` 改为 `localhost`，并同步修改 `ConvenientSystem.Api\appsettings.json` 中 `ConnectionStrings` 的 `Server`。

### 2. 安装前端依赖

```bash
cd web
npm install
```

### 3. 启动开发服务

打开两个终端窗口：

```bash
# 终端 1：后端接口服务（端口 51943）
dev-backend.cmd
```

```bash
# 终端 2：前端热更新（端口 5173，API 代理到 51943）
dev-web.cmd
```

访问 `http://localhost:5173`，默认账号 `admin` / `admin`。

---

## 构建与发布

```bash
# 一键发布：前端构建 → API 打包到 api/ → 桌面端打包到 exe/
publish.cmd
```

发布产物：
| 目录 | 说明 |
|------|------|
| `api/` | 接口服务（单文件自包含，端口 51943） |
| `exe/` | 桌面客户端（WinForms + WebView2，端口 51942，反向代理到 API） |

启动已发布的程序：

```bash
start.cmd
```

---

## 部署

支持两种部署方式：

- **本地部署** — `start.cmd` 同时启动接口服务和桌面客户端
- **云服务器部署** — 接口服务部署到 IIS，桌面客户端连接远端 API（通过 `exe\appsettings.json` 的 `RemoteServerUrl` 配置）

详细部署指南参见 [deploy-aliyun.md](deploy-aliyun.md)（阿里云轻量服务器方案）。

---

## 开发规范

详见 [coding-standards.md](coding-standards.md)，核心要点：

- **先方案后动工** — 新功能先出方案，确认后再编码
- **通用组件优先** — 列表页用 `CommonDataTable`，弹窗用 `CommonDialog`，提示用 `CommonTooltip`
- **DB 变更双维护** — 迁移脚本与 `init.sql` 同步更新
- **SQL 中文注释** — 表和字段必须有中文注释
- **视觉一致性** — 遵循 CSS 变量体系，禁止硬编码颜色值

---

## License

Internal use only.
