namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// API 文档生成器（ApiSpec）DTO 集：C# Controller 源码 → 解析 IR → 多格式 API 数据文件。
    /// IR（中间表示）与格式无关，由各 Exporter 转换为 OpenAPI / Postman / Markdown 等目标格式。
    /// </summary>

    /// <summary>支持的导出格式卡片（来自 DI 注册的全部 IApiExporter，加格式自动出现）。</summary>
    public class ApiSpecFormatDto
    {
        /// <summary>格式标识（如 openapi3-json）。</summary>
        public string Format { get; set; }
        /// <summary>展示名（如 OpenAPI 3.0 (JSON)）。</summary>
        public string DisplayName { get; set; }
        /// <summary>文件扩展名（含点，如 .json）。</summary>
        public string FileExtension { get; set; }
        /// <summary>下载 Content-Type。</summary>
        public string ContentType { get; set; }
        /// <summary>格式说明（适合导入哪些工具）。</summary>
        public string Description { get; set; }
    }

    /// <summary>解决方案扫描出的接口条目（接口级清单，前端勾选后生成文档）。</summary>
    public class ApiSpecSolutionEndpointDto
    {
        /// <summary>所属 Controller 文件（相对解决方案目录的路径，正斜杠分隔）。</summary>
        public string File { get; set; }
        /// <summary>扫描期生成的稳定接口选择标识，用于避免路由文本变化导致筛选失配。</summary>
        public string SelectionKey { get; set; }
        /// <summary>Controller 类名（分组名）。</summary>
        public string Group { get; set; }
        /// <summary>HTTP 方法（GET/POST/PUT/DELETE/PATCH）。</summary>
        public string Method { get; set; }
        /// <summary>接口路径（如 /api/Notice/List）。</summary>
        public string Path { get; set; }
        /// <summary>Action 方法名。</summary>
        public string ActionName { get; set; }
        /// <summary>XML 注释摘要。</summary>
        public string Summary { get; set; }
        /// <summary>PermissionAuthorize 权限码（无则空）。</summary>
        public string Permission { get; set; }
    }
    
    /// <summary>扫描到的 Controller 文件项（相对根目录的路径 + 接口数预览）。</summary>
    public class ApiSpecFileDto
    {
        /// <summary>相对根目录的路径（正斜杠分隔，选中后回传）。</summary>
        public string Path { get; set; }
        /// <summary>Controller 类名（不含 Controller 后缀，如 Notice）。</summary>
        public string ControllerName { get; set; }
        /// <summary>检测到的接口（Action）数量。</summary>
        public int EndpointCount { get; set; }
    }

    /// <summary>接口参数（path / query / header / body / form）。</summary>
    public class ApiSpecParamDto
    {
        /// <summary>参数位置：path / query / header / body / form。</summary>
        public string In { get; set; }
        /// <summary>参数名。</summary>
        public string Name { get; set; }
        /// <summary>原始 C# 类型文本（如 int、string?、List&lt;int&gt;、NoticeDto）。</summary>
        public string TypeText { get; set; }
        /// <summary>是否必填（值类型默认必填；可空/引用类型默认非必填）。</summary>
        public bool Required { get; set; }
        /// <summary>参数注释（XML summary 或行内注释）。</summary>
        public string Description { get; set; }
    }

    /// <summary>解析出的单个接口（Action）。</summary>
    public class ApiSpecEndpointDto
    {
        /// <summary>HTTP 方法（GET/POST/PUT/DELETE/PATCH）。</summary>
        public string Method { get; set; }
        /// <summary>Action 方法名（如 GetList，operationId 用）。</summary>
        public string ActionName { get; set; }
        /// <summary>完整路径（类 Route 前缀 + 方法模板，已解析 [controller] token）。</summary>
        public string Path { get; set; }
        /// <summary>接口摘要（XML summary 首段）。</summary>
        public string Summary { get; set; }
        /// <summary>PermissionAuthorize 权限码（无则空）。</summary>
        public string Permission { get; set; }
        /// <summary>分组名（Controller 类名，如 NoticeController）。</summary>
        public string Group { get; set; }
        /// <summary>扫描期生成的稳定接口选择标识。</summary>
        public string SelectionKey { get; set; }
        /// <summary>参数列表（含 path/query/body）。</summary>
        public List<ApiSpecParamDto> Params { get; set; } = new();
        /// <summary>响应体类型文本（无响应体为空串）。</summary>
        public string ResponseType { get; set; }
    }

    /// <summary>DTO 类型字段。</summary>
    public class ApiSpecFieldDto
    {
        /// <summary>字段/属性名。</summary>
        public string Name { get; set; }
        /// <summary>原始 C# 类型文本。</summary>
        public string TypeText { get; set; }
        /// <summary>是否必填（值类型 true，引用类型 false）。</summary>
        public bool Required { get; set; }
        /// <summary>注释。</summary>
        public string Description { get; set; }
    }

    /// <summary>解析出的 DTO 类型定义（class/record 的字段列表，或枚举值列表）。</summary>
    public class ApiSpecTypeDto
    {
        /// <summary>类型名（如 NoticeDto）。</summary>
        public string Name { get; set; }
        /// <summary>类型注释。</summary>
        public string Comment { get; set; }
        /// <summary>是否枚举。</summary>
        public bool IsEnum { get; set; }
        /// <summary>枚举成员名。</summary>
        public List<string> EnumValues { get; set; } = new();
        /// <summary>字段列表（非枚举）。</summary>
        public List<ApiSpecFieldDto> Fields { get; set; } = new();
    }

    /// <summary>
    /// 解析文档（IR）：选中的 Controller 集合解析出的接口清单 + 引用到的全部 DTO 类型树。
    /// 各格式 Exporter 以此为唯一输入。
    /// </summary>
    public class ApiSpecDocumentDto
    {
        /// <summary>文档标题（默认项目名）。</summary>
        public string Title { get; set; }
        /// <summary>文档版本。</summary>
        public string Version { get; set; }
        /// <summary>服务器地址（写进 servers / baseUrl 变量）。</summary>
        public string BaseUrl { get; set; }
        /// <summary>接口列表（按 Controller 分组排序）。</summary>
        public List<ApiSpecEndpointDto> Endpoints { get; set; } = new();
        /// <summary>引用到的 DTO 类型（键为类型名，含递归依赖）。</summary>
        public Dictionary<string, ApiSpecTypeDto> Types { get; set; } = new();
        /// <summary>解析警告（如类型未找到、循环引用被截断），前端可展示。</summary>
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>导出/预览结果（内容字符串 + 下载元信息）。</summary>
    public class ApiSpecExportDto
    {
        /// <summary>建议文件名（如 api-spec.openapi3.json）。</summary>
        public string FileName { get; set; }
        /// <summary>Content-Type。</summary>
        public string ContentType { get; set; }
        /// <summary>生成内容（文本）。</summary>
        public string Content { get; set; }
        /// <summary>解析警告。</summary>
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>生成预览/导出请求体：将长 only 参数从 URL 移到 body，避免 HTTP 414。</summary>
    public class ApiSpecPreviewRequest
    {
        public string RootDir { get; set; } = "";
        public string Files { get; set; } = "";
        public string Format { get; set; } = "";
        public string? Title { get; set; }
        public string? BaseUrl { get; set; }
        /// <summary>原始解决方案文件或目录路径，用于保持扫描与生成的项目范围一致。</summary>
        public string? SolutionPath { get; set; }
        /// <summary>扫描期返回的接口选择标识集合，优先于兼容字段 Only 使用。</summary>
        public List<string> SelectionKeys { get; set; } = new();
        /// <summary>旧版接口筛选键，保留以兼容已有调用。</summary>
        public string? Only { get; set; }
    }

    /// <summary>当前用户的 Apifox Access Token 保存状态，不返回任何令牌内容。</summary>
    public class ApifoxAccessTokenStatusDto
    {
        public bool TokenConfigured { get; set; }
    }

    /// <summary>保存或清除当前用户的 Apifox Access Token。</summary>
    public class ApifoxAccessTokenSaveRequest
    {
        public string? AccessToken { get; set; }
        public bool ClearAccessToken { get; set; }
    }

    /// <summary>向 Apifox 导入 OpenAPI 数据的请求。内容由桌面端本地解析生成，导入选项仅本次使用。</summary>
    public class ApifoxImportRequest
    {
        public string Content { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public long? TargetEndpointFolderId { get; set; }
        public long? TargetBranchId { get; set; }
        public string EndpointOverwriteBehavior { get; set; } = "AUTO_MERGE";
    }

    /// <summary>Apifox 导入结果摘要。</summary>
    public class ApifoxImportResultDto
    {
        public int EndpointCreated { get; set; }
        public int EndpointUpdated { get; set; }
        public int EndpointFailed { get; set; }
        public int EndpointIgnored { get; set; }
        public int SchemaCreated { get; set; }
        public int SchemaUpdated { get; set; }
        public int SchemaFailed { get; set; }
        public int SchemaIgnored { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
