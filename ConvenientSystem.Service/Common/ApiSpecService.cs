using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConvenientSystem.Service.Common.ApiSpec;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// API 文档生成器服务：Roslyn 纯语法解析（无需编译引用/语义模型）。
    /// 流程：扫描 *Controller.cs → 提取 [Route]/[HttpXxx]/参数/返回类型/XML 注释 → IR 文档，
    /// 同时全项目建类型名索引，递归解析接口引用到的 DTO 字段树（含嵌套/泛型/枚举）。
    /// </summary>
    public class ApiSpecService : IApiSpecService
    {
        private readonly IEnumerable<IApiExporter> _exporters;

        public ApiSpecService(IEnumerable<IApiExporter> exporters)
        {
            _exporters = exporters;
        }

        /// <summary>扫描时排除的目录（产物/依赖缓存，含大量无关 .cs）。</summary>
        private static readonly string[] ExcludedDirs = { "bin", "obj", "node_modules", ".git", ".vs", "dist", "exe", "installer-output" };

        /// <summary>识别的 HTTP 方法特性名。</summary>
        private static readonly string[] HttpAttrNames = { "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions" };

        public List<ApiSpecFormatDto> GetFormats()
            => _exporters.Select(e => new ApiSpecFormatDto
               {
                   Format = e.Format,
                   DisplayName = e.DisplayName,
                   FileExtension = e.FileExtension,
                   ContentType = e.ContentType,
                   Description = e.Description,
               }).ToList();

        public List<ApiSpecFileDto> ScanControllers(string rootDir)
        {
            var dir = ValidateRoot(rootDir);
            var result = new List<ApiSpecFileDto>();
            foreach (var file in EnumerateCsFiles(dir))
            {
                if (!Path.GetFileName(file).EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)) continue;

                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                var controllerClasses = tree.GetCompilationUnitRoot()
                    .DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Where(c => c.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal));

                var total = 0;
                var controllerName = "";
                foreach (var cls in controllerClasses)
                {
                    total += CountEndpoints(cls);
                    if (controllerName.Length == 0)
                        controllerName = cls.Identifier.ValueText[..^"Controller".Length];
                }
                if (total == 0) continue; // 无接口的 Controller 跳过

                result.Add(new ApiSpecFileDto
                {
                    Path = Path.GetRelativePath(dir, file).Replace('\\', '/'),
                    ControllerName = controllerName,
                    EndpointCount = total,
                });
            }
            return result.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 扫描解决方案内全部接口，返回接口级清单（前端勾选后生成文档）。
        /// 传 .sln/.slnx 文件：解析项目列表，仅扫描这些项目目录；传目录：按根目录全扫。
        /// 只做纯语法解析（路由/方法/注释），不建类型索引不展开 DTO，与文件级扫描同等成本。
        /// </summary>
        public List<ApiSpecSolutionEndpointDto> ScanSolution(string solutionPath)
        {
            var scope = ResolveSolutionScope(solutionPath);
            var rootDir = scope.RootDir;
            var projectDirs = scope.ProjectDirs;

            // 类名索引（跨项目）：基类路由解析用——[Route]/[Area] 可能声明在任意 .cs（如 BaseController.cs）
            var typeIndex = BuildTypeIndex(projectDirs);

            var result = new List<ApiSpecSolutionEndpointDto>();
            foreach (var dir in projectDirs)
            {
                foreach (var file in EnumerateCsFiles(dir))
                {
                    if (!Path.GetFileName(file).EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)) continue;

                    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                    foreach (var group in tree.GetCompilationUnitRoot()
                                 .DescendantNodes().OfType<ClassDeclarationSyntax>()
                                 .Where(c => c.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal))
                                 .GroupBy(c => c.Identifier.ValueText))
                    {
                        // 路由拼接与 ParseController 保持一致：自身 [Route]/[Area] 优先，缺失沿基类链补齐
                        var controllerToken = group.Key[..^"Controller".Length];
                        var (routePrefix, area) = ResolveRouteAndArea(group, typeIndex);

                        foreach (var method in group.SelectMany(c => c.Members.OfType<MethodDeclarationSyntax>()))
                        {
                            if (!method.Modifiers.Any(m => m.ValueText == "public")) continue;
                            var httpAttr = GetAttributes(method).FirstOrDefault(a => HttpAttrNames.Contains(a.Name.ToString()));
                            if (httpAttr == null) continue;

                            var relativeFile = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                            result.Add(new ApiSpecSolutionEndpointDto
                            {
                                File = relativeFile,
                                SelectionKey = CreateSelectionKey(relativeFile, method),
                                Group = group.Key,
                                Method = httpAttr.Name.ToString()["Http".Length..].ToUpperInvariant(),
                                Path = CombineRoute(routePrefix, GetLiteral(httpAttr) ?? "", controllerToken, method.Identifier.ValueText, area),
                                ActionName = method.Identifier.ValueText,
                                Summary = ExtractSummary(method),
                                Permission = GetPermission(method),
                            });
                        }
                    }
                }
            }
            if (result.Count == 0) throw new BizException("未扫描到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）");
            return result.OrderBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Method).ToList();
        }

        /// <summary>解析解决方案文件中的项目相对路径（.slnx 为 XML 格式，.sln 为文本格式）。</summary>
        private static (string RootDir, List<string> ProjectDirs) ResolveSolutionScope(string solutionPath)
        {
            var path = (solutionPath ?? "").Trim().Trim('"');
            if (path.Length == 0) throw new BizException("请填写解决方案文件路径");

            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".sln" && ext != ".slnx") throw new BizException("仅支持 .sln / .slnx 解决方案文件，或直接填目录路径");
                var rootDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                var projectDirs = ParseSolutionProjects(path)
                    .Select(project => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(rootDir, project)))!)
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (projectDirs.Count == 0) throw new BizException("解决方案里未找到任何项目");
                return (rootDir, projectDirs);
            }

            if (Directory.Exists(path))
            {
                var rootDir = Path.GetFullPath(path);
                return (rootDir, new List<string> { rootDir });
            }

            throw new BizException($"路径不存在：{path}");
        }

        private static (string RootDir, List<string> ProjectDirs) ResolveParseScope(string rootDir, string? solutionPath)
        {
            var root = ValidateRoot(rootDir);
            if (string.IsNullOrWhiteSpace(solutionPath)) return (root, new List<string> { root });

            var scope = ResolveSolutionScope(solutionPath);
            if (!string.Equals(Path.TrimEndingDirectorySeparator(scope.RootDir), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
                throw new BizException("解决方案路径与项目根目录不一致，请重新扫描后再生成");
            return scope;
        }

        private static List<string> ParseSolutionProjects(string solutionPath)
        {
            if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return XDocument.Load(solutionPath)
                    .Descendants("Project")
                    .Select(p => (string?)p.Attribute("Path"))
                    .Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p!.Replace('/', '\\')).ToList();
            }

            // .sln 文本格式：Project("GUID") = "名称", "相对路径.csproj", "{GUID}"
            return Regex.Matches(File.ReadAllText(solutionPath),
                    @"Project\(""[^""]*""\)\s*=\s*""[^""]*"",\s*""(?<path>[^""]+\.csproj)""")
                .Select(m => m.Groups["path"].Value).ToList();
        }

        public ApiSpecDocumentDto Parse(string rootDir, string files, string? title, string? baseUrl, string? solutionPath = null)
        {
            var scope = ResolveParseScope(rootDir, solutionPath);
            var dir = scope.RootDir;
            var selected = SplitFiles(files);
            if (selected.Count == 0) throw new BizException("未选择任何 Controller 文件");

            // 与扫描阶段使用相同项目范围，避免同名基类解析到不同路由。
            var typeIndex = BuildTypeIndex(scope.ProjectDirs);

            var doc = new ApiSpecDocumentDto
            {
                Title = string.IsNullOrWhiteSpace(title) ? "ConvenientSystem API" : title.Trim(),
                Version = "1.0.0",
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost" : baseUrl.Trim().TrimEnd('/'),
            };

            // 解析每个选中的 Controller（partial 声明按类名合并）
            foreach (var relPath in selected)
            {
                var absPath = SafeCombine(dir, relPath);
                if (!File.Exists(absPath))
                {
                    doc.Warnings.Add($"文件不存在，已跳过：{relPath}");
                    continue;
                }

                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(absPath)).GetCompilationUnitRoot();
                foreach (var group in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                             .Where(c => c.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal))
                             .GroupBy(c => c.Identifier.ValueText))
                {
                    ParseController(doc, typeIndex, relPath, group.Key, group);
                }
            }

            if (doc.Endpoints.Count == 0) throw new BizException("选中文件里未解析到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）");

            doc.Endpoints = doc.Endpoints
                .OrderBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Method).ToList();
            return doc;
        }

        public ApiSpecExportDto Export(string rootDir, string files, string format, string? title, string? baseUrl,
            string? only = null, string? solutionPath = null, IEnumerable<string>? selectionKeys = null)
        {
            var exporter = _exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
                ?? throw new BizException($"不支持的导出格式：{format}");
            var doc = Parse(rootDir, files, title, baseUrl, solutionPath);

            // 优先使用扫描期返回的稳定选择标识，避免路由文本在两次解析间变化造成筛选失配。
            var selectedKeys = selectionKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);
            if (selectedKeys is { Count: > 0 })
            {
                doc.Endpoints = doc.Endpoints.Where(e => selectedKeys.Contains(e.SelectionKey)).ToList();
                if (doc.Endpoints.Count == 0) throw new BizException("所选接口未包含在解析结果里，请重新扫描后勾选");
            }
            // 兼容已有调用：未传稳定标识时，继续按旧的路由键筛选。
            else if (!string.IsNullOrWhiteSpace(only))
            {
                var picked = new HashSet<string>(
                    only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.Ordinal);
                doc.Endpoints = doc.Endpoints.Where(e => picked.Contains($"{e.Group}|{e.Method}|{e.Path}")).ToList();
                if (doc.Endpoints.Count == 0) throw new BizException("所选接口未包含在解析结果里，请重新扫描后勾选");
            }

            return new ApiSpecExportDto
            {
                FileName = exporter.FileNameBase + exporter.FileExtension,
                ContentType = exporter.ContentType,
                Content = exporter.Export(doc),
                Warnings = doc.Warnings,
            };
        }

        // ========== Controller / Action 解析 ==========

        private static string CreateSelectionKey(string sourceFile, MethodDeclarationSyntax method)
            => $"{sourceFile.Replace('\\', '/')}|{method.SpanStart}";

        private static void ParseController(ApiSpecDocumentDto doc, Dictionary<string, SyntaxNode> typeIndex,
            string sourceFile, string className, IEnumerable<ClassDeclarationSyntax> declarations)
        {
            var groupName = className;
            var controllerToken = className[..^"Controller".Length];

            // 与 ScanSolution 相同的路由解析：自身 [Route]/[Area] 优先，缺失沿基类链补齐（保证 only 键对齐）
            var (routePrefix, area) = ResolveRouteAndArea(declarations, typeIndex);

            foreach (var cls in declarations)
            {
                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(m => m.ValueText == "public")) continue;

                    var httpAttr = GetAttributes(method).FirstOrDefault(a => HttpAttrNames.Contains(a.Name.ToString()));
                    if (httpAttr == null) continue;

                    var verb = httpAttr.Name.ToString()["Http".Length..].ToUpperInvariant();
                    var methodTemplate = GetLiteral(httpAttr) ?? "";
                    var fullPath = CombineRoute(routePrefix, methodTemplate, controllerToken, method.Identifier.ValueText, area);

                    var ep = new ApiSpecEndpointDto
                    {
                        Method = verb,
                        ActionName = method.Identifier.ValueText,
                        Path = fullPath,
                        Summary = ExtractSummary(method),
                        Permission = GetPermission(method),
                        Group = groupName,
                        SelectionKey = CreateSelectionKey(sourceFile, method),
                        ResponseType = UnwrapReturnType(method.ReturnType?.ToString() ?? ""),
                    };
                    ep.Params = ParseParameters(method, fullPath);
                    doc.Endpoints.Add(ep);

                    // 参数与响应类型引用的 DTO 入队解析
                    foreach (var p in ep.Params) CollectTypeNames(p.TypeText, doc, typeIndex);
                    if (ep.ResponseType.Length > 0) CollectTypeNames(ep.ResponseType, doc, typeIndex);
                }
            }
        }

        /// <summary>统计类中带 HTTP 特性的 public 方法数。</summary>
        private static int CountEndpoints(ClassDeclarationSyntax cls)
            => cls.Members.OfType<MethodDeclarationSyntax>()
                .Count(m => m.Modifiers.Any(x => x.ValueText == "public")
                            && GetAttributes(m).Any(a => HttpAttrNames.Contains(a.Name.ToString())));

        /// <summary>
        /// 路由拼接（遵循 ASP.NET Core 属性路由规则）：
        /// 方法模板以 / 或 ~ 开头时忽略类前缀；[controller] 替换为类名去 Controller 后缀；
        /// [action] 替换方法名；[area] 替换 [Area] 特性值（缺失时替换为空，并规范化多余斜杠）。
        /// </summary>
        private static string CombineRoute(string routePrefix, string methodTemplate, string controllerToken, string actionName, string area)
        {
            // 无条件替换全部 ASP.NET Core 路由占位符；area 为空时移除占位符本身。
            routePrefix = routePrefix
                .Replace("[area]", area)
                .Replace("[controller]", controllerToken)
                .Replace("[action]", actionName);
            methodTemplate = methodTemplate
                .Replace("[area]", area)
                .Replace("[controller]", controllerToken)
                .Replace("[action]", actionName);

            // 清除因空 area 产生的连续/或前后斜杠，例如 "/api//DeptImport" 或 "api/" 等。
            routePrefix = NormalizeRouteSegment(routePrefix);
            methodTemplate = NormalizeRouteTemplate(methodTemplate);

            string combined;
            if (methodTemplate.StartsWith("~"))
                combined = methodTemplate[1..];
            else if (methodTemplate.StartsWith("/"))
                combined = methodTemplate;
            else if (routePrefix.Length > 0 && methodTemplate.Length > 0)
                combined = routePrefix.TrimEnd('/') + "/" + methodTemplate.TrimStart('/');
            else
                combined = (routePrefix + methodTemplate).Trim('/');

            return "/" + combined.Trim('/');
        }

        private static string NormalizeRouteSegment(string value)
        {
            value = value.Trim('/');
            // 移除空 area 导致的双斜杠：api/[area]/[controller] -> api//Home -> api/Home
            value = Regex.Replace(value, @"/+", "/");
            return value;
        }

        private static string NormalizeRouteTemplate(string value)
        {
            if (value.StartsWith("~") || value.StartsWith("/"))
                return value;
            value = value.Trim('/');
            value = Regex.Replace(value, @"/+", "/");
            return value;
        }

        /// <summary>
        /// 解析类级 [Route] 模板与 [Area] 值：自身声明优先（partial 任一声明带特性即生效），
        /// 缺失时沿基类继承链向上补齐（子类覆盖基类，符合 ASP.NET Core 属性路由语义）。
        /// 基类可能在任意 .cs 文件（如 BaseController.cs），经全项目类型索引定位；
        /// 基类在 NuGet 包等扫描范围之外时无法解析，路由将为空。
        /// </summary>
        private static (string Route, string Area) ResolveRouteAndArea(IEnumerable<ClassDeclarationSyntax> declarations, Dictionary<string, SyntaxNode> typeIndex)
        {
            var route = "";
            var area = "";

            foreach (var cls in declarations)
            {
                var attrs = GetAttributes(cls);
                if (route.Length == 0)
                {
                    var r = attrs.FirstOrDefault(a => a.Name.ToString() == "Route");
                    if (r != null) route = GetLiteral(r) ?? "";
                }
                if (area.Length == 0)
                {
                    var ar = attrs.FirstOrDefault(a => a.Name.ToString() == "Area");
                    if (ar != null) area = GetLiteral(ar) ?? "";
                }
            }

            // 沿基类链补齐缺失项（visited 防循环继承；基类名去命名空间前缀与泛型参数后查索引）
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = declarations.FirstOrDefault(c => c.BaseList != null);
            while (current != null && (route.Length == 0 || area.Length == 0))
            {
                var baseName = current.BaseList?.Types.FirstOrDefault()?.Type.ToString() ?? "";
                var simple = baseName.Split('<')[0].Split('.').Last().Trim();
                if (simple.Length == 0 || !visited.Add(simple)) break;
                if (!typeIndex.TryGetValue(simple, out var node) || node is not ClassDeclarationSyntax baseCls) break;

                var attrs = GetAttributes(baseCls);
                if (route.Length == 0)
                {
                    var r = attrs.FirstOrDefault(a => a.Name.ToString() == "Route");
                    if (r != null) route = GetLiteral(r) ?? "";
                }
                if (area.Length == 0)
                {
                    var ar = attrs.FirstOrDefault(a => a.Name.ToString() == "Area");
                    if (ar != null) area = GetLiteral(ar) ?? "";
                }
                current = baseCls;
            }

            return (route, area);
        }

        /// <summary>返回类型解包：Task&lt;T&gt;/ActionResult&lt;T&gt; 逐层剥壳，IActionResult/ActionResult/void/xxxResult → 空串。</summary>
        private static string UnwrapReturnType(string returnType)
        {
            var t = returnType.Trim();
            while (t.Length > 0)
            {
                if (t == "void" || t == "Task" || t is "ActionResult" or "IActionResult" || t.EndsWith("Result")) return "";

                if (ApiSpecSchemaMapper.TryParseGeneric(t, out var name, out var args) && args.Count == 1
                    && (name == "Task" || name == "ActionResult"))
                {
                    t = args[0].Trim();
                    continue;
                }
                return t;
            }
            return "";
        }

        private static List<ApiSpecParamDto> ParseParameters(MethodDeclarationSyntax method, string fullPath)
        {
            var result = new List<ApiSpecParamDto>();
            foreach (var p in method.ParameterList.Parameters)
            {
                var typeText = p.Type?.ToString() ?? "object";
                if (typeText == "CancellationToken") continue;

                var attrs = GetAttributes(p).Select(a => a.Name.ToString()).ToList();
                string in_ = attrs.FirstOrDefault(a => a is "FromQuery" or "FromRoute" or "FromBody" or "FromForm" or "FromHeader") switch
                {
                    "FromQuery" => "query",
                    "FromRoute" => "path",
                    "FromHeader" => "header",
                    "FromForm" => "form",
                    "FromBody" => "body",
                    _ => "",
                };

                // 无特性时按 ASP.NET Core 模型绑定规则推断：
                // 路径模板含 {name} → path；基元类型 → query；IFormFile → form；复杂类型 → body
                if (in_.Length == 0)
                {
                    var name = p.Identifier.ValueText;
                    var pathTokens = Regex.Matches(fullPath, @"\{(\w+):?\w*\}")
                        .Select(m => m.Groups[1].Value).ToHashSet();
                    if (pathTokens.Contains(name)) in_ = "path";
                    else if (typeText.Contains("IFormFile")) in_ = "form";
                    else if (IsSimpleType(typeText)) in_ = "query";
                    else in_ = "body";
                }

                result.Add(new ApiSpecParamDto
                {
                    In = in_,
                    Name = p.Identifier.ValueText,
                    TypeText = typeText,
                    // 有默认值或可空类型 → 非必填；值类型 → 必填
                    Required = p.Default == null && !typeText.EndsWith("?") && IsValueType(typeText),
                    Description = ExtractSummary(p),
                });
            }
            return result;
        }

        /// <summary>基元/已知简单类型（无特性时推断为 query 绑定）。</summary>
        private static bool IsSimpleType(string typeText)
        {
            var t = typeText.Trim().TrimEnd('?');
            return t is "int" or "long" or "short" or "byte" or "sbyte" or "ushort" or "ulong"
                or "decimal" or "double" or "float" or "bool" or "char" or "string" or "Guid"
                or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan";
        }

        /// <summary>值类型（决定参数/字段默认必填）。</summary>
        private static bool IsValueType(string typeText)
        {
            var t = typeText.Trim();
            return t is "int" or "long" or "short" or "byte" or "sbyte" or "ushort" or "ulong"
                or "decimal" or "double" or "float" or "bool" or "char" or "Guid"
                or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan";
        }

        /// <summary>提取 PermissionAuthorize 特性的权限码参数。</summary>
        private static string GetPermission(MethodDeclarationSyntax method)
        {
            var attr = GetAttributes(method).FirstOrDefault(a => a.Name.ToString() == "PermissionAuthorize");
            return GetLiteral(attr) ?? "";
        }

        // ========== DTO 类型树解析 ==========

        /// <summary>全项目类型名索引（class/struct/record/enum 声明，含 Shared/Model 下的 DTO）。</summary>
        private static Dictionary<string, SyntaxNode> BuildTypeIndex(string rootDir)
            => BuildTypeIndex(new[] { rootDir });

        /// <summary>多目录版索引：解决方案扫描跨多个项目目录建索引（基类路由解析）。</summary>
        private static Dictionary<string, SyntaxNode> BuildTypeIndex(IEnumerable<string> rootDirs)
        {
            var index = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
            foreach (var rootDir in rootDirs)
            foreach (var file in EnumerateCsFiles(rootDir))
            {
                SyntaxNode root;
                try { root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot(); }
                catch { continue; }

                foreach (var node in root.DescendantNodes())
                {
                    string? name = node switch
                    {
                        ClassDeclarationSyntax c => c.Identifier.ValueText,
                        StructDeclarationSyntax s => s.Identifier.ValueText,
                        RecordDeclarationSyntax r => r.Identifier.ValueText,
                        InterfaceDeclarationSyntax i => i.Identifier.ValueText,
                        EnumDeclarationSyntax e => e.Identifier.ValueText,
                        _ => null,
                    };
                    if (name != null && !index.ContainsKey(name)) index[name] = node;
                }
            }
            return index;
        }

        /// <summary>
        /// 把类型文本里引用的自定义类型加入解析队列（泛型逐层拆出标识符，
        /// 基元/集合类型名忽略，已知类型跳过），随后逐个解析为 ApiSpecTypeDto。
        /// </summary>
        private static void CollectTypeNames(string typeText, ApiSpecDocumentDto doc, Dictionary<string, SyntaxNode> typeIndex)
        {
            var queue = new Queue<string>();
            foreach (var id in ExtractIdentifiers(typeText)) queue.Enqueue(id);
            var visited = new HashSet<string>(doc.Types.Keys, StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (name.Length == 0 || IsSimpleType(name) || !visited.Add(name)) continue;
                if (name is "List" or "IList" or "IEnumerable" or "ICollection" or "IReadOnlyList" or "IReadOnlyCollection"
                    or "HashSet" or "Dictionary" or "Nullable" or "Task" or "ActionResult" or "IActionResult"
                    or "Queue" or "Stack" or "LinkedList" or "KeyValuePair" or "IFormFile" or "JsonElement" or "JsonNode" or "JsonDocument"
                    or "object" or "dynamic") continue;

                if (!typeIndex.TryGetValue(name, out var decl))
                {
                    var warning = $"类型 {name} 未在扫描范围内找到，已按 object 处理";
                    if (!doc.Warnings.Contains(warning)) doc.Warnings.Add(warning);
                    continue;
                }

                var typeDto = new ApiSpecTypeDto { Name = name, Comment = ExtractSummary(decl) };
                if (decl is EnumDeclarationSyntax enumDecl)
                {
                    typeDto.IsEnum = true;
                    foreach (var member in enumDecl.Members.OfType<EnumMemberDeclarationSyntax>())
                        typeDto.EnumValues.Add(member.Identifier.ValueText);
                }
                else if (decl is TypeDeclarationSyntax typeDecl)
                {
                    foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
                    {
                        var fieldType = prop.Type?.ToString() ?? "object";
                        typeDto.Fields.Add(new ApiSpecFieldDto
                        {
                            Name = prop.Identifier.ValueText,
                            TypeText = fieldType,
                            Required = !fieldType.EndsWith("?") && IsValueType(fieldType),
                            Description = ExtractSummaryOrInlineComment(prop),
                        });
                        foreach (var id in ExtractIdentifiers(fieldType)) queue.Enqueue(id);
                    }
                    foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
                    {
                        if (field.Modifiers.Any(m => m.ValueText == "const" || m.ValueText == "static")) continue;
                        var fieldType = field.Declaration.Type.ToString();
                        foreach (var v in field.Declaration.Variables)
                        {
                            typeDto.Fields.Add(new ApiSpecFieldDto
                            {
                                Name = v.Identifier.ValueText,
                                TypeText = fieldType,
                                Required = !fieldType.EndsWith("?") && IsValueType(fieldType),
                                Description = ExtractSummaryOrInlineComment(field),
                            });
                        }
                        foreach (var id in ExtractIdentifiers(fieldType)) queue.Enqueue(id);
                    }
                }
                else continue;

                // 先注册（自身/循环引用时 $ref 可解析），再继续展开后续依赖
                doc.Types[name] = typeDto;
            }
        }

        /// <summary>提取类型文本里的全部标识符（List&lt;PageDto&lt;UserDto&gt;&gt; → List/PageDto/UserDto）。</summary>
        private static IEnumerable<string> ExtractIdentifiers(string typeText)
            => Regex.Matches(typeText ?? "", @"[A-Za-z_][A-Za-z0-9_]*").Select(m => m.Value);

        // ========== 通用语法辅助 ==========

        private static IEnumerable<AttributeSyntax> GetAttributes(SyntaxNode node)
            => node switch
            {
                MethodDeclarationSyntax m => m.AttributeLists.SelectMany(l => l.Attributes),
                ClassDeclarationSyntax c => c.AttributeLists.SelectMany(l => l.Attributes),
                ParameterSyntax p => p.AttributeLists.SelectMany(l => l.Attributes),
                _ => Enumerable.Empty<AttributeSyntax>(),
            };

        /// <summary>取特性第一个字符串字面量参数（[Route("api")]/[HttpGet("List")]）。</summary>
        private static string? GetLiteral(AttributeSyntax? attr)
        {
            var expr = attr?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            return expr is LiteralExpressionSyntax lit ? lit.Token.ValueText : null;
        }

        /// <summary>XML 注释 summary 提取（支持多行，剥掉 /// 前缀后拼接为单行）。</summary>
        private static string ExtractSummary(SyntaxNode node)
        {
            var trivia = node.GetLeadingTrivia().ToFullString();
            return ExtractSummaryFromTrivia(trivia);
        }

        private static string ExtractSummaryFromTrivia(string trivia)
        {
            var m = Regex.Match(trivia, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
            if (!m.Success) return "";
            var lines = m.Groups[1].Value.Split('\n')
                .Select(l => Regex.Replace(l, @"^\s*///\s?", ""))
                .Select(l => Regex.Replace(l, @"\s*</?param[^>]*>", ""))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);
            return string.Join(" ", lines);
        }

        /// <summary>属性/字段注释：优先 XML summary，回退行尾 // 注释。</summary>
        private static string ExtractSummaryOrInlineComment(SyntaxNode node)
        {
            var summary = ExtractSummary(node);
            if (summary.Length > 0) return summary;
            var trailing = node.GetTrailingTrivia().ToFullString();
            var m = Regex.Match(trailing, @"//\s?(.*)");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        // ========== 文件系统辅助 ==========

        private static string ValidateRoot(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) throw new BizException("请填写项目根目录");
            var dir = Path.GetFullPath(rootDir.Trim().Trim('"'));
            if (!Directory.Exists(dir)) throw new BizException($"目录不存在：{dir}");
            return dir;
        }

        private static List<string> SplitFiles(string files)
            => (files ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>拼接相对路径并禁止跳出根目录（.. 直通被拒）。</summary>
        private static string SafeCombine(string rootDir, string relPath)
        {
            var abs = Path.GetFullPath(Path.Combine(rootDir, relPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!abs.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                throw new BizException($"路径越界：{relPath}");
            return abs;
        }

        private static IEnumerable<string> EnumerateCsFiles(string rootDir)
        {
            var excluded = new HashSet<string>(ExcludedDirs, StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                    .Any(seg => excluded.Contains(seg)));
        }
    }
}
