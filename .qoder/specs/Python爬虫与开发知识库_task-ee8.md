# Python 爬虫服务 + 开发知识库 集成方案

## 摘要

两件事独立交付、互不阻塞，共用同一套现有基建（菜单驱动路由、菜单 Name 即权限码、Hangfire 调度、init.sql 幂等建表）。

**核心判断**：项目已有 C# 爬虫（`LotteryDrawCrawlJob`、`LotteryRuleCrawlJob`，静态 `HttpClient` 直连接口），跑得稳，**不迁移**。Python 只承接 C# 吃力的场景：JS 渲染页面、需要登录/模拟点击、复杂 HTML 解析（parsel/pandas）。判断标准写进文档：**目标站有干净 JSON 接口 → 继续用 C#；只有渲染后的 DOM → 交给 Python**。

---

## 一、Python 爬虫服务

### 1.1 架构与通信

```
Hangfire(C#) SpiderRunJob [Queue("spider")]
  ├─ 读 SpiderDefinition 表取配置（URL、参数、超时）
  ├─ HttpClient POST http://127.0.0.1:51944/run  ← Python FastAPI 常驻
  ├─ 校验返回 JSON → C# 写业务表（审计字段、双库路由都在 C# 侧）
  └─ 写 SpiderRunLog（状态/耗时/抓取条数/入库条数/错误摘要）
```

**决策 1：常驻 FastAPI，不用进程调用。** 理由：Playwright 浏览器实例可常驻复用（冷启动 2–5 秒，按次调用会把这个成本乘到每个任务上）；能返回进度。代价是多一个进程要保活，由 1.4 解决。

**决策 2：Python 不碰数据库，只返回结构化数据。** 连接串只留在 C# 一处；`CreatedById`、双库路由（`ConvenientSystemDb` / `YhSystemDb`）逻辑已在 C# 侧；Python 崩溃不会写脏数据。约定单次返回上限 5000 行，超量在 Python 侧分页、C# 循环拉取。

**统一返回契约**（C# 强类型反序列化，字段不得增删）：
```json
{ "ok": true, "spiderKey": "demo_news", "rows": [{}], "count": 20, "elapsedMs": 1234, "message": null }
```

**决策 3：Python 服务只监听 127.0.0.1，并校验共享密钥 header**（`X-Spider-Token`，值配在 `appsettings.json` 的 `Spider:Token`，Python 侧从环境变量读取，两边由 start.cmd 传递）。抓取 URL 必须来自 `SpiderDefinition` 表，**不接受调用方传任意 URL**，避免被当作内网探测跳板。

### 1.2 Hangfire 独立队列（必做，否则会拖垮现有任务）

现状：`ServicesExtent.cs:125` `WorkerCount = 2`，未配置 Queues，短信/邮件/主机监控/网站监控全挤在 default 队列。爬虫动辄几十秒到几分钟，直接注册会把 2 个 worker 占满，导致短信邮件延迟发出。

改法：`AddHangfireServer` 改为两个 server 实例 —— 原有的保持 default 队列 `WorkerCount = 2`；新增一个 `Queues = ["spider"]`、`WorkerCount = 2`、`ServerName = "ConvenientSystem-Spider"`。`SpiderRunJob` 标注 `[Queue("spider")]`。

### 1.3 目录结构与运行时分发

**决策 4：内嵌 Python embeddable 包**，目标机免装、版本锁定。

```
python/
├── runtime/            # embeddable python 3.12 + site-packages（不进 Git）
├── app/
│   ├── main.py         # FastAPI 入口：/health /spiders /run
│   ├── registry.py     # spiderKey → 爬虫类 映射
│   ├── schema.py       # 统一返回结构
│   └── spiders/
│       ├── base.py     # 基类：fetch(静态) / render(浏览器，预留) / parse
│       └── demo_news.py
├── requirements.txt    # 锁版本：fastapi uvicorn httpx parsel
└── setup-python.cmd    # 下载 embeddable + pip install 到 runtime/
```

**决策 5：一期只做静态爬取**（httpx + parsel，依赖仅几 MB），`base.py` 预留 `render()` 钩子。将来要浏览器渲染时，只加一个 Playwright 引擎实现，不用推翻结构 —— 但 Playwright + Chromium 会让分发体积从约 60MB 涨到 400MB+，届时再决定。

`.gitignore` 追加 `python/runtime/`；`publish.cmd` 在第 5 步后增加：拷贝 `python\` 到发布根（排除 `__pycache__`）；`start.cmd` 增加第 3 个窗口启动 Python 服务，与现有两个窗口风格一致（可见日志、关窗即停），并沿用其 `:islistening 51944` 就绪等待子过程。

### 1.4 保活与告警

一期：`start.cmd` 启动，C# 侧 `PythonSpiderClient` 每次调用前打 `/health`；连续失败则 `SpiderRunLog` 记失败并通过现有 Webhook 通道（`Shared/Common/Webhook`）推送告警。不做自动重启 —— 静默重启会掩盖真实故障。二期视稳定性再考虑由 C# 托管子进程（复用 `HostMonitorCheckJob.RunPsProcessAsync` 的 `ProcessStartInfo` 模式：UTF-8 输出捕获、超时、`KillQuietly`）。

### 1.5 数据库（init.sql 幂等追加 + 中文注释，遵循现有 `usp_AddColumnComment` 方式）

- `SpiderDefinition`：Key（唯一）、名称、目标 URL、参数 JSON、Cron、超时秒、是否启用、目标表标识、创建人
- `SpiderRunLog`：SpiderKey、开始/结束时间、状态、抓取条数、入库条数、错误摘要（NVARCHAR(MAX)）
- 采集结果表按业务单独建（如 `SpiderNewsItem`），**不做万能宽表** —— 宽表会让后续查询和索引无从下手

### 1.6 新增文件（照 `SmsTemplate` 样板）

| 层 | 路径 |
|---|---|
| Entity | `Shared/Entity/Spider/SpiderDefinitionEntity.cs`、`SpiderRunLogEntity.cs` |
| Model | `Shared/Model/Spider/SpiderDto.cs` |
| Client | `Shared/Common/Spider/PythonSpiderClient.cs`（静态 HttpClient，与现有 Job 风格一致） |
| Job | `Shared/Jobs/SpiderRunJob.cs`（`[Queue("spider")]` + `[AutomaticRetry(3)]`） |
| Interface/Service | `Service/Interface/Spider/ISpiderService.cs`、`Service/Spider/SpiderService.cs` |
| Controller | `Api/Controllers/Spider/SpiderController.cs`（`[Area("Spider")]` + `[PermissionAuthorize("spider-definition")]`） |
| 前端 | `web/src/spider/{api/spider.ts, types.ts, views/SpiderDefinitionView.vue, views/SpiderRunLogView.vue}` |
| 注册 | `ServicesExtent.AddBusinessServices` 注册 Service 与 Job；init.sql 插 SysMenu（`spider-definition`、`spider-log`）+ SysRoleMenu |

前端列表页使用 `CommonDataTable`，按钮行进插槽，表格内部滚动（遵循既有规范）。

---

## 二、开发知识库

**决策 6：做成系统内模块，不做独立静态站。** 已有登录/权限/审计/标签体系可直接复用；独立 VitePress 站要另起部署与鉴权，且无法与错误日志、SQL 工具互相跳转。

### 2.1 数据库

- `KbCategory`：树形分类（ParentId、排序、名称）
- `KbArticle`：标题、分类 Id、Markdown 正文（`[Column(StringLength = -1)]`）、标签（逗号分隔）、浏览数、是否置顶、创建人、更新时间
- `KbArticleVersion`：正文快照 + 版本号 + 修改人 + 修改时间 —— 知识库最常见的诉求是"改坏了要回滚"，一期就要有

### 2.2 搜索

一期：后端 `LIKE %kw%`（标题 + 正文 + 标签）+ 分类过滤，前端标题列表复用本次新增的 [pinyin.ts](file:///e:/A-Chenzk/MyProject/ConvenientSystem/web/src/common/pinyin.ts) 做拼音过滤。数据量千级以内完全够用，**不上 SQL Server 全文索引** —— 全文索引要额外的目录维护和部署步骤，收益要到万级数据才体现。

### 2.3 前端

- 编辑器：复用已装的 **monaco-editor**（已有 CodeEditor 页面先例）做 Markdown 编辑，左编辑右预览
- **需新增前端依赖**：`markdown-it`（渲染）+ `highlight.js`（代码块高亮）。这是整个方案唯一必须新增的前端依赖
- 页面：`web/src/kb/views/{KbListView.vue（左分类树 + 右列表）, KbEditView.vue, KbDetailView.vue}`
- 菜单权限码：`kb-article`、`kb-category`

---

## 三、分期与验收

| 期 | 范围 | 验收标准 |
|---|---|---|
| 一期 | Python 服务骨架 + 1 个示例爬虫 + spider 独立队列 + 定义/日志两个页面 | 页面上点「立即执行」，能看到 SpiderRunLog 出现成功记录且抓取条数 > 0；同时发一条测试短信验证 default 队列未被阻塞 |
| 二期 | 知识库 CRUD + Markdown 编辑/预览 + 版本回滚 + 搜索 | 建条目、改后回滚到上一版、按拼音搜到标题 |
| 三期（可选） | 爬取结果 → 知识库草稿；Playwright 引擎；语义检索 | 视一期实际稳定性再评估 |

## 四、需要你拍板的取舍（已给默认值，不同意就指出来）

1. **通信方式** = 常驻 FastAPI（备选：进程调用，省一个常驻进程，但每次冷启动）
2. **落库方** = C# 落库（备选：Python 直连库，少一次 JSON 传输，但连接串扩散到两处）
3. **运行时** = 内嵌 embeddable Python（备选：PyInstaller 打 exe，分发干净但改代码就要重打包）
4. **爬取能力** = 静态爬取 + 预留浏览器扩展位（备选：一期直接上 Playwright）
5. **知识库** = 系统内模块（备选：VitePress 静态站 / 追加 AI 语义检索）
6. **端口** = Python 服务占 51944（现有 51942 桌面壳、51943 接口服务）

## 五、风险

- **杀软误报**：Windows 上 `python.exe` 常驻 + 对外抓取，容易被安全软件拦截，部署前需在目标机确认放行
- **发布体积**：一期约 +60MB；若二期上 Playwright 会到 400MB+，需确认分发方式能承受
- **爬取合规**：目标站的 robots 与访问频率必须在 `SpiderDefinition` 里配置限速，避免把对方站点打挂或 IP 被封
