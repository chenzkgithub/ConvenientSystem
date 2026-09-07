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
}
