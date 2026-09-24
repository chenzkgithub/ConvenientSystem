using ConvenientSystem.Service.Common;
using ConvenientSystem.Service.Common.ApiSpec;
using ConvenientSystem.Service.Common.SqlQuery;
using ConvenientSystem.Service.Ai;
using ConvenientSystem.Service.Email;
using ConvenientSystem.Service.Sms;
using ConvenientSystem.Service.YunHan;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Audit;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Filters;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Jobs;
using FreeSql;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;

namespace ConvenientSystem.Api
{
    /// <summary>
    /// 服务注册扩展：接口服务的全部依赖注入登记集中在此，Program.cs 只负责宿主启动。
    /// 控制器仅注入 Service 层接口，具体实现在本类中逐条登记。
    /// </summary>
    public static class ServicesExtent
    {
        /// <summary>
        /// 登记全部服务（数据库、定时任务、短信/邮件基础设施、业务服务）。
        /// </summary>
        public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // 全局异常过滤器：Service 层抛出的 BizException 统一转为 { message } 响应体。
            services.AddControllers(options => options.Filters.Add<BizExceptionFilter>());

            // 服务端敏感个人配置（如 Apifox Access Token）使用 Data Protection 加密后才落 UserConfig。
            // 密钥环必须持久化到应用目录 keys 子目录（容器内 /app/keys，部署时挂命名卷）：
            // 默认存进程内部目录，容器重建即丢失，历史加密的 Token 全部无法解密，每次部署都需重新保存。
            var keysDir = Path.Combine(AppContext.BaseDirectory, "keys");
            services.AddDataProtection()
                .SetApplicationName("ConvenientSystem")
                .PersistKeysToFileSystem(new DirectoryInfo(keysDir));
            services.AddHttpClient("Apifox", client =>
            {
                client.BaseAddress = new Uri("https://api.apifox.com/");
                client.Timeout = TimeSpan.FromMinutes(2);
            });

            // 当前用户上下文：供 Service 层读取登录用户与管理员标记，用于数据权限隔离。
            services.AddHttpContextAccessor();
            services.AddSingleton<ICurrentUser, CurrentUser>();

            // 内存日志缓冲：供实时日志查看器使用
            var logBuffer = new ConvenientSystem.Api.Middleware.MemoryLogBuffer();
            services.AddSingleton(logBuffer);
            services.AddLogging(logging => logging.AddProvider(new ConvenientSystem.Api.Middleware.MemoryLoggerProvider(logBuffer)));

            var dbType = GetDbType(configuration);
            var configConnStr = AddDatabases(services, configuration, dbType);
            AddAuthInfrastructure(services, configuration);
            AddHangfire(services, configConnStr, dbType);
            AddSmsInfrastructure(services);
            AddEmailInfrastructure(services);
            AddLotteryServices(services);
            AddWebMonitorServices(services);
            AddAuditInfrastructure(services);
            AddWebhookInfrastructure(services);
            AddBusinessServices(services);
        }

        /// <summary>读取配置中的数据库类型（SqlServer 或 Sqlite），默认 SqlServer。</summary>
        private static string GetDbType(IConfiguration configuration)
            => configuration.GetValue<string>("Database:Type") ?? "SqlServer";

        /// <summary>
        /// 数据库连接：业务库 IFreeSql（单例）、本地配置库（Keyed 单例）、SQL 工具动态数据源工厂。
        /// Sqlite 模式下业务库复用配置库连接（云服务器无法访问内网 SQL Server）。
        /// </summary>
        /// <returns>本地配置库连接字符串，供 Hangfire 持久化复用。</returns>
        private static string AddDatabases(IServiceCollection services, IConfiguration configuration, string dbType)
        {
            var configConnStr = configuration.GetConnectionString("ConvenientSystemDb")
                ?? throw new InvalidOperationException("未配置数据库连接字符串 ConvenientSystemDb，请检查 appsettings.json。");

            // 业务库（YhSystemDb）：SqlServer 模式下使用独立连接；Sqlite 模式下复用配置库连接。
            var yhConnStr = configuration.GetConnectionString("YhSystemDb");
            if (string.IsNullOrWhiteSpace(yhConnStr) || dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                yhConnStr = configConnStr;

            services.AddSingleton<IFreeSql>(sp => BuildFreeSql(sp, yhConnStr, "FreeSql", dbType));

            services.AddKeyedSingleton<IFreeSql>("ConvenientSystemDb",
                (sp, _) => BuildFreeSql(sp, configConnStr, "FreeSql(配置库)", dbType));

            // SQL 查询工具动态数据源的 IFreeSql 工厂。
            services.AddSingleton<DynamicFreeSqlFactory>();

            return configConnStr;
        }

        /// <summary>构建 IFreeSql 实例（关闭自动建表，SQL 执行写 Debug 日志）。</summary>
        private static IFreeSql BuildFreeSql(IServiceProvider sp, string connStr, string logTag, string dbType)
        {
            var dataType = dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
                ? DataType.Sqlite
                : DataType.SqlServer;

            var fsql = new FreeSqlBuilder()
                .UseConnectionString(dataType, connStr)
                .UseAutoSyncStructure(false)
                .Build();

            fsql.Aop.CurdAfter += (s, e) =>
            {
                var logger = sp.GetRequiredService<ILogger<IFreeSql>>();
                logger.LogDebug("{Tag} SQL执行：\n{Sql}\n耗时{Elapsed}ms", logTag, e.Sql, e.ElapsedMilliseconds);
            };
            return fsql;
        }

        /// <summary>
        /// JWT 认证/授权：Bearer Token 校验（对称密钥 HMAC-SHA256）。
        /// 密钥解析与 LoginService 一致：环境变量 JWT_KEY → SysConfig 表 → 内置缺省。
        /// 启动时从 DB 读取，DB 不可用时回退缺省值。
        /// </summary>
        private static void AddAuthInfrastructure(IServiceCollection services, IConfiguration configuration)
        {
            var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
                ?? ReadJwtKeyFromDb(configuration)
                ?? "ConvenientSystem-Default-Jwt-Key-please-change-in-production";

            // 注册 JWT 密钥持有者（单例）：LoginService（签发方）与 TokenValidationParameters（验证方）
            // 共用同一实例，避免启动时 DB 不可用导致 ReadJwtKeyFromDb 回退默认值、
            // 而 LoginService 从 DB 读到不同值时密钥不一致——不一致会令签发的 token 无法通过验证。
            services.AddSingleton(new JwtKeyHolder(jwtKey));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = JwtHelper.BuildValidationParameters(jwtKey);

                    // SignalR WebSocket 连接无法携带 Authorization 头：从 query 的 access_token 读取（仅限聊天 Hub 路径）
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                            {
                                context.Token = accessToken;
                            }
                            return Task.CompletedTask;
                        }
                    };
                });
            services.AddAuthorization();

            // 聊天实时推送：Hub 连接的用户标识（从 JWT claim 取 Guid，供 Clients.User 定向推送）
            services.AddSignalR();
            services.AddSingleton<IUserIdProvider, SignalRUserIdProvider>();
        }

        /// <summary>
        /// 启动时从 SysConfig 表读取 JWT 密钥（DI 尚未构建，用 ADO 直连）。
        /// DB 不可用时返回 null，由调用方走内置缺省。
        /// </summary>
        private static string? ReadJwtKeyFromDb(IConfiguration configuration)
        {
            var connStr = configuration.GetConnectionString("ConvenientSystemDb");
            var dbType = GetDbType(configuration);
            if (string.IsNullOrEmpty(connStr)) return null;
            try
            {
                if (dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    using var conn = new SqliteConnection(connStr);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT ConfigValue FROM SysConfig WHERE ConfigKey = 'Jwt.Key' LIMIT 1";
                    var result = cmd.ExecuteScalar()?.ToString();
                    return string.IsNullOrEmpty(result) ? null : result;
                }
                else
                {
                    using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT TOP 1 ConfigValue FROM dbo.SysConfig WHERE ConfigKey = N'Jwt.Key'";
                    var result = cmd.ExecuteScalar()?.ToString();
                    return string.IsNullOrEmpty(result) ? null : result;
                }
            }
            catch { return null; }
        }

        /// <summary>定时任务调度：SqlServer 持久化存储 / Sqlite 模式用 InMemory（重启后由启动补偿恢复任务）。</summary>
        private static void AddHangfire(IServiceCollection services, string hangfireConnStr, string dbType)
        {
            services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings();

                if (dbType.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                    config.UseInMemoryStorage();
                else
                    config.UseSqlServerStorage(hangfireConnStr, new SqlServerStorageOptions
                    {
                        QueuePollInterval = TimeSpan.FromSeconds(5),
                        SchemaName = "Hangfire",
                        SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance
                    });
            });
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 2;
                options.ServerName = "ConvenientSystem";
            });
        }

        /// <summary>短信基础设施：Provider 策略（注册全部服务商，运行时按配置动态选择）、配额、启动补偿。</summary>
        private static void AddSmsInfrastructure(IServiceCollection services)
        {
            services.AddSingleton<ISmsProvider, AliyunSmsProvider>();
            services.AddSingleton<ISmsProvider, IhuyiSmsProvider>();
            services.AddSingleton<ISmsProviderFactory>(sp =>
            {
                var fsql = sp.GetRequiredKeyedService<IFreeSql>("ConvenientSystemDb");
                var providers = sp.GetServices<ISmsProvider>();
                return new SmsProviderFactory(fsql, providers);
            });
            services.AddSingleton<ISmsQuotaService, SmsQuotaService>();
            services.AddSingleton<SmsStartupCompensator>();
        }

        /// <summary>审计基础设施：内存队列（单例）+ 后台批量落库服务 + 每日定时清理服务 + 在线用户追踪器 + 会话令牌存储（挤号）。</summary>
        private static void AddAuditInfrastructure(IServiceCollection services)
        {
            services.AddSingleton<AuditLogQueue>();
            services.AddHostedService<AuditLogBackgroundService>();
            services.AddHostedService<AuditLogCleanupService>();
            services.AddSingleton<OnlineUserTracker>();
            services.AddSingleton<SessionTokenStore>();
        }

        /// <summary>群机器人基础设施：注册全部 Provider（钉钉/企业微信/飞书）+ 工厂（运行时按配置选择）。</summary>
        private static void AddWebhookInfrastructure(IServiceCollection services)
        {
            services.AddSingleton<IWebhookProvider, DingTalkProvider>();
            services.AddSingleton<IWebhookProvider, DingTalkPrivateProvider>();  // 钉钉私聊机器人
            services.AddSingleton<IWebhookProvider, WeComProvider>();
            services.AddSingleton<IWebhookProvider, WeComPrivateProvider>();    // 企业微信私聊
            services.AddSingleton<IWebhookProvider, FeishuProvider>();
            services.AddSingleton<IWebhookProvider, FeishuPrivateProvider>();   // 飞书私聊
            services.AddSingleton<WebhookProviderFactory>(sp =>
                new WebhookProviderFactory(sp.GetServices<IWebhookProvider>()));
            services.AddSingleton<WebhookNotifier>();
        }

        /// <summary>邮件基础设施：SMTP 发送、Hangfire 作业、启动补偿。</summary>
        private static void AddEmailInfrastructure(IServiceCollection services)
        {
            services.AddSingleton<IEmailService, EmailService>();
            services.AddSingleton<EmailSendJob>();
            services.AddSingleton<EmailStartupCompensator>();
        }

        /// <summary>彩票模块：开奖数据爬取 + 玩法规则抓取 + 开奖结果邮件通知 Hangfire 定时任务（直连福彩/体彩官网接口）</summary>
        private static void AddLotteryServices(IServiceCollection services)
        {
            // 注册 Hangfire Job
            services.AddSingleton<LotteryDrawCrawlJob>();
            services.AddSingleton<LotteryRuleCrawlJob>();
            services.AddSingleton<LotteryResultNotifyJob>();
            services.AddSingleton<LotteryRandomPickJob>();
            services.AddSingleton<LotteryStartupCompensator>();
        }

        /// <summary>网站/API 监控：定时巡检探测 Job + 启动补偿（注册每分钟巡检任务）</summary>
        private static void AddWebMonitorServices(IServiceCollection services)
        {
            services.AddSingleton<WebMonitorCheckJob>();
            services.AddSingleton<WebMonitorStartupCompensator>();
        }

        /// <summary>业务服务：控制器只注入接口，具体实现集中在此登记。</summary>
        private static void AddBusinessServices(IServiceCollection services)
        {
            // Common 模块
            services.AddSingleton<ILoginService, LoginService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IViewService, ViewService>();
            services.AddSingleton<ILockService, LockService>();
            services.AddSingleton<IAuditLogService, AuditLogService>();
            services.AddSingleton<IErrorLogService, ErrorLogService>();
            services.AddSingleton<ISystemDashboardService, SystemDashboardService>();
            services.AddSingleton<IHangfireService, HangfireService>();
            services.AddSingleton<IJobExecutionLogService, JobExecutionLogService>();
            services.AddSingleton<INotifyService, NotifyService>();
            services.AddSingleton<IWebhookLogService, WebhookLogService>();
            services.AddSingleton<IUserManageService, UserManageService>();

            // 手机端权限（SysUserAppPerm 白名单：目录/用户列表/差量保存，与 PC 端角色权限完全独立）
            services.AddSingleton<IAppPermService, AppPermService>();
            services.AddSingleton<IRoleService, RoleService>();
            services.AddSingleton<IProfileService, ProfileService>();

            // 系统通知（站内通知 + 发布时联动邮件/短信/群机器人推送）
            services.AddSingleton<INoticeService, NoticeService>();
            services.AddSingleton<NoticePushJob>();

            // 大乐透选号记录
            services.AddSingleton<ILotteryService, LotteryService>();

            // 大乐透开奖记录与走势图
            services.AddSingleton<ILotteryDrawService, LotteryDrawService>();

            // 开奖结果每日汇总（邮件/群机器人/详情页共用）
            services.AddSingleton<ILotterySummaryService, LotterySummaryService>();

            // 彩票玩法规则（奖级对照表与规则版本审核）
            services.AddSingleton<ILotteryRuleService, LotteryRuleService>();

            // 彩票智能分析（多维度评分与号码推荐）
            services.AddSingleton<ILotteryAnalysisService, LotteryAnalysisService>();

            // 系统配置（键值对配置管理，DB 优先 appsettings 兜底）
            services.AddSingleton<ISysConfigService, SysConfigService>();

            // 用户个人配置（当前登录用户的个性化配置，覆盖全局 SysConfig）
            services.AddScoped<IUserConfigService, UserConfigService>();
            // Apifox 配置存于当前用户 UserConfig，Token 仅在服务端解密并代调官方导入接口。
            services.AddScoped<IApifoxImportService, ApifoxImportService>();

            // 统一异步任务中心（全部耗时操作的进度表；云端经 SignalR 定向推送进度）
            services.AddSingleton<IAsyncTaskNotifier, SignalRAsyncTaskNotifier>();
            services.AddSingleton<AsyncTaskCenter>();

            // 外部公开页面（免登录 standalone=1 页面管理）
            services.AddSingleton<ISysPublicPageService, SysPublicPageService>();

            // 即时聊天（好友模式单聊+群聊：会话/消息/屏蔽/转发，实时推送由 Api 层 ChatHub 编排）
            services.AddSingleton<IChatService, ChatService>();

            // 好友（搜索/申请/处理/删除；通讯规则校验在 ChatService，实时推送由 Api 层 ChatHub 编排）
            services.AddSingleton<IFriendService, FriendService>();

            // 代码命名转换（百度翻译 API 优先，MyMemory 回退，前端拼音兜底）
            services.AddSingleton<ICodeNamingService, CodeNamingService>();

            // 雪花ID生成器（线程安全单例，开发工具集调用）
            services.AddSingleton<ISnowflakeIdService, SnowflakeIdService>();

            // 网站/API 监控
            services.AddSingleton<IWebMonitorService, WebMonitorService>();

            // SQL 查询工具（Schema/Script/Execute 均依赖 IDataSourceService 解析数据源）
            services.AddSingleton<IDataSourceService, DataSourceService>();
            services.AddSingleton<ISqlExecuteService, SqlExecuteService>();
            services.AddSingleton<ISchemaService, SchemaService>();
            services.AddSingleton<ISqlScriptService, SqlScriptService>();
            services.AddSingleton<ISqlSnippetService, SqlSnippetService>();
            services.AddSingleton<ISqlFavoriteService, SqlFavoriteService>();
            // AI 辅助 SQL（NL2SQL）：依赖 Scoped 的模型/用户配置服务，故注册为 Scoped
            services.AddScoped<IAiSqlService, AiSqlService>();

            // 短信模块
            services.AddSingleton<ISmsConfigService, SmsConfigService>();
            services.AddSingleton<ISmsTemplateService, SmsTemplateService>();
            services.AddSingleton<ISmsTaskService, SmsTaskService>();
            services.AddSingleton<ISmsLogService, SmsLogService>();

            // 邮件模块
            services.AddSingleton<IEmailConfigService, EmailConfigService>();
            services.AddSingleton<IEmailTaskService, EmailTaskService>();

            // 云汉考勤
            services.AddSingleton<IAttendanceService, AttendanceService>();

            // Web 前端版本包管理（桌面客户端更新）
            services.AddSingleton<IWebPackageService, WebPackageService>();

            // 桌面安装包自更新
            services.AddSingleton<IDesktopUpdateService, DesktopUpdateService>();

            // 配置文件热编辑（白名单校验 + 自动备份 + 安全写入）
            services.AddSingleton<IConfigEditorService, ConfigEditorService>();

            // 代码扫描（正则规则引擎 + Git Diff 增量扫描）
            services.AddScoped<ICodeScanService, CodeScanService>();

            // AI 能力 P0：OpenAI 兼容 Provider 通吃 DeepSeek/通义/Kimi/GLM/Ollama/vLLM；
            // 模型管理/对话均为 Scoped（依赖用户上下文），生成注册表跨请求存在；
            // 流式 chunk 经 ChatHub AiResponseChunk 事件推送，前端轮询 Messages 兜底
            services.AddHttpClient("Ai", client => client.Timeout = TimeSpan.FromSeconds(300))
                .SetHandlerLifetime(TimeSpan.FromMinutes(10));
            services.AddSingleton<IAiCompletionProvider, OpenAiCompatibleProvider>();
            services.AddSingleton<AiGenerationRegistry>();
            services.AddSingleton<IAiChunkNotifier, SignalRAiChunkNotifier>();
            services.AddScoped<IAiModelService, AiModelService>();
            services.AddScoped<IAiChatService, AiChatService>();
            services.AddSingleton<AiToolService>();

            // API 文档生成器（C# Controller 源码 → OpenAPI/Postman/Markdown）
            services.AddSingleton<IApiSpecService, ApiSpecService>();
            // 接口调试代理（服务端转发调试请求，规避浏览器 CORS）
            services.AddSingleton<IApiDebugService, ApiDebugService>();
            // 各格式导出器（加格式 = 新实现类 + 注册一行，格式列表自动出现）
            // 注意：using 父命名空间不会导入子命名空间，必须单独 using ...ApiSpec 才能用这些类型
            services.AddSingleton<IApiExporter, OpenApiJsonExporter>();
            services.AddSingleton<IApiExporter, OpenApiYamlExporter>();
            services.AddSingleton<IApiExporter, PostmanExporter>();
            services.AddSingleton<IApiExporter, MarkdownExporter>();
        }
    }
}
