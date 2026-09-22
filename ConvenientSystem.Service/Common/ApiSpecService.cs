using System.Collections.Concurrent;
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
    /// 同时全项目建类型索引，递归解析接口引用到的 DTO 字段树（含嵌套/泛型/枚举）。
    /// 扫描与解析生成以后台任务执行（统一 AsyncTaskCenter 进度表，见 StartScan/StartGenerate），
    /// 一次解析同时产出 IR 文档与导出内容，避免旧 Parse+Preview 组合的全量双解析。
    /// </summary>
    public class ApiSpecService : IApiSpecService
    {
        private readonly IEnumerable<IApiExporter> _exporters;
        private readonly AsyncTaskCenter _center;

        public ApiSpecService(IEnumerable<IApiExporter> exporters, AsyncTaskCenter center)
        {
            _exporters = exporters;
            _center = center;
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
        /// 纯语法解析（路由/方法/注释/命名空间），不展开 DTO；report 上报逐文件进度（后台任务用，可空）。
        /// </summary>
        private List<ApiSpecSolutionEndpointDto> ScanSolutionCore(string solutionPath, Action<string, int, int, string>? report)
        {
            var scope = ResolveSolutionScope(solutionPath);
            var rootDir = scope.RootDir;
            var projectDirs = scope.ProjectDirs;

            // 类名索引（跨项目）：基类路由解析用——[Route]/[Area] 可能声明在任意 .cs（如 BaseController.cs）
            var typeIndex = BuildTypeIndex(projectDirs,
                (completed, total, file) => report?.Invoke("indexing", completed, total, file));

            // Controller 文件先收集再处理：进度总数可知
            var controllerFiles = new List<string>();
            foreach (var dir in projectDirs)
                foreach (var file in EnumerateCsFiles(dir))
                    if (Path.GetFileName(file).EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
                        controllerFiles.Add(file);

            var result = new List<ApiSpecSolutionEndpointDto>();
            var processed = 0;
            foreach (var file in controllerFiles)
            {
                report?.Invoke("scanning", processed, controllerFiles.Count, Path.GetFileName(file));
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                foreach (var group in tree.GetCompilationUnitRoot()
                             .DescendantNodes().OfType<ClassDeclarationSyntax>()
                             .Where(c => c.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal))
                             .GroupBy(c => c.Identifier.ValueText))
                {
                    // 路由拼接与 ParseController 保持一致：自身 [Route]/[Area] 优先，缺失沿基类链补齐
                    var controllerToken = group.Key[..^"Controller".Length];
                    var (routePrefix, area) = ResolveRouteAndArea(group, typeIndex);
                    var ns = ExtractNamespace(group);

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
                            Namespace = ns,
                            Method = httpAttr.Name.ToString()["Http".Length..].ToUpperInvariant(),
                            Path = CombineRoute(routePrefix, GetLiteral(httpAttr) ?? "", controllerToken, method.Identifier.ValueText, area),
                            ActionName = method.Identifier.ValueText,
                            Summary = ExtractSummary(method),
                            Permission = GetPermission(method),
                        });
                    }
                }
                processed++;
            }
            report?.Invoke("scanning", processed, controllerFiles.Count, "");
            if (result.Count == 0) throw new BizException("未扫描到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）");
            return result.OrderBy(e => e.Namespace, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
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

        /// <summary>
        /// 解析选中的 Controller 集合 → IR 文档；report 上报逐文件进度（后台任务用，可空）。
        /// 旧公开 Parse 端点已由后台任务 StartGenerate 取代，本方法仅内部使用。
        /// </summary>
        private ApiSpecDocumentDto ParseCore(string rootDir, string files, string? title, string? baseUrl,
            string? solutionPath, Action<string, int, int, string>? report)
        {
            var scope = ResolveParseScope(rootDir, solutionPath);
            var dir = scope.RootDir;
            var selected = SplitFiles(files);
            if (selected.Count == 0) throw new BizException("未选择任何 Controller 文件");

            // 与扫描阶段使用相同项目范围，避免同名基类解析到不同路由。
            var typeIndex = BuildTypeIndex(scope.ProjectDirs,
                (completed, total, file) => report?.Invoke("indexing", completed, total, file));

            var doc = new ApiSpecDocumentDto
            {
                Title = string.IsNullOrWhiteSpace(title) ? "ConvenientSystem API" : title.Trim(),
                Version = "1.0.0",
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8030" : baseUrl.Trim().TrimEnd('/'),
            };

            // 解析每个选中的 Controller（partial 声明按类名合并）
            var processed = 0;
            foreach (var relPath in selected)
            {
                report?.Invoke("parsing", processed, selected.Count, Path.GetFileName(relPath));
                processed++;
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
            report?.Invoke("parsing", processed, selected.Count, "");

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
            ResolveExporter(format);
            var doc = ParseCore(rootDir, files, title, baseUrl, solutionPath, null);
            doc.Endpoints = FilterEndpoints(doc.Endpoints, selectionKeys, only);
            return ExportDocument(doc, format);
        }

        // ========== 后台任务（扫描 / 解析生成）==========

        /// <summary>启动解决方案扫描后台任务：同步校验路径后返回任务初始快照，扫描在后台执行。</summary>
        public AsyncTaskDto StartScan(ApiSpecScanTaskRequest request, Guid? userId)
        {
            // 同步校验路径（解析 .sln 项目列表，毫秒级）：失败立即报错，而不是任务启动后才失败
            var solutionPath = (request?.SolutionPath ?? "").Trim();
            ResolveSolutionScope(solutionPath);

            var handle = _center.Begin(userId, "apispec-scan", "解决方案扫描");
            _ = Task.Run(() => RunScan(handle, solutionPath));
            return _center.GetTask(handle.TaskId, userId)!;
        }

        /// <summary>启动解析并生成后台任务：一次解析同时产出 IR 文档与指定格式导出内容。</summary>
        public AsyncTaskDto StartGenerate(ApiSpecGenerateRequest request, Guid? userId)
        {
            var req = request ?? new ApiSpecGenerateRequest();
            ResolveExporter(req.Format); // 格式非法立即报错
            if (SplitFiles(req.Files).Count == 0) throw new BizException("未选择任何 Controller 文件");
            ResolveParseScope(req.RootDir, req.SolutionPath); // 目录/一致性校验

            var handle = _center.Begin(userId, "apispec-generate", "解析并生成 API 文档");
            _ = Task.Run(() => RunGenerate(handle, req));
            return _center.GetTask(handle.TaskId, userId)!;
        }

        /// <summary>复用已完成生成任务的解析结果按新格式/标题重新导出（不重新解析源码）。</summary>
        public ApiSpecExportDto ReExport(ApiSpecReExportRequest request, Guid? userId)
        {
            if (request == null) throw new BizException("生成任务不存在或已过期，请重新解析生成");

            ApiSpecDocumentDto? doc = null;
            var keys = new List<string>();
            var found = _center.Read(request.TaskId, userId, p =>
            {
                if (p.Kind != "apispec-generate" || p.Status != "succeeded"
                    || p.Result is not ApiSpecGenerateTaskResult r)
                    return false;
                doc = r.Document;
                keys = p.Payload as List<string> ?? new List<string>();
                return true;
            });
            if (!found || doc is null)
                throw new BizException("生成任务不存在或已过期，请重新解析生成");

            var view = new ApiSpecDocumentDto
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? doc.Title : request.Title.Trim(),
                Version = doc.Version,
                BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? doc.BaseUrl : request.BaseUrl.Trim().TrimEnd('/'),
                Endpoints = FilterEndpoints(doc.Endpoints, keys, null),
                Types = doc.Types,
                Warnings = doc.Warnings,
            };
            return ExportDocument(view, request.Format);
        }

        private void RunScan(AsyncTaskCenter.AsyncTaskHandle handle, string solutionPath)
        {
            try
            {
                var endpoints = ScanSolutionCore(solutionPath, (phase, completed, total, current) =>
                    _center.Update(handle, p =>
                    {
                        p.Phase = phase;
                        p.Completed = completed;
                        p.Total = total;
                        p.Current = current;
                    }));
                _center.Update(handle, p =>
                {
                    p.Phase = "";
                    p.Current = "";
                    p.Result = new ApiSpecScanTaskResult { Endpoints = endpoints };
                    p.Summary = $"扫描到 {endpoints.Count} 个接口"; // 轮询快照不带 Result，摘要供任务面板/完成通知展示
                    p.Status = "succeeded";
                });
            }
            catch (Exception ex)
            {
                _center.Fail(handle, ex.Message);
            }
            finally
            {
                _center.Finish(handle);
            }
        }

        private void RunGenerate(AsyncTaskCenter.AsyncTaskHandle handle, ApiSpecGenerateRequest req)
        {
            try
            {
                var doc = ParseCore(req.RootDir, req.Files, req.Title, req.BaseUrl, req.SolutionPath,
                    (phase, completed, total, current) =>
                        _center.Update(handle, p =>
                        {
                            p.Phase = phase;
                            p.Completed = completed;
                            p.Total = total;
                            p.Current = current;
                        }));

                var exporter = ResolveExporter(req.Format);
                _center.Update(handle, p =>
                {
                    p.Phase = "exporting";
                    p.Total = 1;
                    p.Completed = 0;
                    p.Current = $"正在生成 {exporter.DisplayName} 内容...";
                });

                var view = new ApiSpecDocumentDto
                {
                    Title = doc.Title,
                    Version = doc.Version,
                    BaseUrl = doc.BaseUrl,
                    Endpoints = FilterEndpoints(doc.Endpoints, req.SelectionKeys, null),
                    Types = doc.Types,
                    Warnings = doc.Warnings,
                };
                var export = ExportDocument(view, req.Format);

                _center.Update(handle, p =>
                {
                    // 选择标识存 Payload（不序列化）：ReExport 换格式导出时按同一勾选集合筛选
                    p.Payload = req.SelectionKeys?
                        .Where(key => !string.IsNullOrWhiteSpace(key)).ToList() ?? new List<string>();
                    p.Phase = "";
                    p.Current = "";
                    p.Total = 1;
                    p.Completed = 1;
                    p.Result = new ApiSpecGenerateTaskResult { Document = doc, Export = export }; // Document 为未筛选的完整 IR，终态经 GetResult 端点按需拉取
                    p.Summary = $"已生成 {view.Endpoints.Count} 个接口的文档"; // 按勾选筛选后的数量，轮询快照不带 Result
                    p.Status = "succeeded";
                });
            }
            catch (Exception ex)
            {
                _center.Fail(handle, ex.Message);
            }
            finally
            {
                _center.Finish(handle);
            }
        }

        /// <summary>按选择标识（优先）或旧路由键筛选接口；无筛选条件时原样返回。</summary>
        private static List<ApiSpecEndpointDto> FilterEndpoints(List<ApiSpecEndpointDto> endpoints,
            IEnumerable<string>? selectionKeys, string? only)
        {
            // 优先使用扫描期返回的稳定选择标识，避免路由文本在两次解析间变化造成筛选失配。
            var keys = selectionKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);
            if (keys is { Count: > 0 })
            {
                var filtered = endpoints.Where(e => keys.Contains(e.SelectionKey)).ToList();
                if (filtered.Count == 0) throw new BizException("所选接口未包含在解析结果里，请重新扫描后勾选");
                return filtered;
            }
            // 兼容已有调用：未传稳定标识时，继续按旧的路由键筛选。
            if (!string.IsNullOrWhiteSpace(only))
            {
                var picked = new HashSet<string>(
                    only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.Ordinal);
                var filtered = endpoints.Where(e => picked.Contains($"{e.Group}|{e.Method}|{e.Path}")).ToList();
                if (filtered.Count == 0) throw new BizException("所选接口未包含在解析结果里，请重新扫描后勾选");
                return filtered;
            }
            return endpoints;
        }

        private IApiExporter ResolveExporter(string format)
            => _exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
               ?? throw new BizException($"不支持的导出格式：{format}");

        private ApiSpecExportDto ExportDocument(ApiSpecDocumentDto doc, string format)
        {
            var exporter = ResolveExporter(format);
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

        private static void ParseController(ApiSpecDocumentDto doc, ApiTypeIndex typeIndex,
            string sourceFile, string className, IEnumerable<ClassDeclarationSyntax> declarations)
        {
            var groupName = className;
            var controllerToken = className[..^"Controller".Length];

            // 命名空间与扫描阶段共用同一提取逻辑，保证列表分组与导入 Apifox 的目录前缀一致
            var ns = ExtractNamespace(declarations);

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
                        Namespace = ns,
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

        /// <summary>
        /// Controller 所在命名空间：取首个非空声明（partial 合并场景），剥掉尾部 .Controllers 段（目录名冗余）。
        /// 扫描与解析共用，保证前端分组与 Apifox 导入 tag 前缀一致。
        /// </summary>
        private static string ExtractNamespace(IEnumerable<ClassDeclarationSyntax> declarations)
        {
            var ns = declarations.Select(d => d.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                    .FirstOrDefault()?.Name.ToString() ?? "").FirstOrDefault(n => n.Length > 0) ?? "";
            return ns.EndsWith(".Controllers", StringComparison.Ordinal) ? ns[..^".Controllers".Length] : ns;
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
        private static (string Route, string Area) ResolveRouteAndArea(IEnumerable<ClassDeclarationSyntax> declarations, ApiTypeIndex typeIndex)
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

            // 沿基类链补齐缺失项（visited 防循环继承；基类名去命名空间前缀与泛型参数后查索引）。
            // 基类按 当前类命名空间.基类名 精确解析：解决方案内多项目都有同名 BaseController，
            // 简单名匹配会让先扫到的项目顶替其余项目的基类（Area/Route 张冠李戴）。
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = declarations.FirstOrDefault(c => c.BaseList != null);
            while (current != null && (route.Length == 0 || area.Length == 0))
            {
                var baseName = current.BaseList?.Types.FirstOrDefault()?.Type.ToString() ?? "";
                var simple = baseName.Split('<')[0].Split('.').Last().Trim();
                if (simple.Length == 0 || !visited.Add(simple)) break;
                if (!TryResolveBaseClass(current, simple, typeIndex, out var baseCls)) break;

                // TryResolveBaseClass 返回 true 时 baseCls 必非 null，断言消除可空警告
                var attrs = GetAttributes(baseCls!);
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
        private static ApiTypeIndex BuildTypeIndex(string rootDir)
            => BuildTypeIndex(new[] { rootDir });

        /// <summary>多目录版索引：解决方案扫描跨多个项目目录建索引（基类路由解析）。report 上报逐文件进度（可空）。</summary>
        private static ApiTypeIndex BuildTypeIndex(IEnumerable<string> rootDirs, Action<int, int, string>? report = null)
        {
            // 双键索引：ByFullName 按 命名空间.类型名 精确匹配；BySimpleName 保留简单名首现匹配（DTO 展开兜底）。
            // 同一解决方案内各项目常有同名基类（如每个 Api 项目各自的 BaseController），只按简单名索引
            // 会让先扫到的项目顶替其余全部项目的基类，导致 [Route]/[Area] 张冠李戴。
            var index = new ApiTypeIndex();
        
            // 先收集后处理：进度总数可知
            var files = new List<string>();
            foreach (var rootDir in rootDirs)
                files.AddRange(EnumerateCsFiles(rootDir));
        
            // 计数器与 switch 模式变量 i（InterfaceDeclarationSyntax）同名会触发 CS0136，改名 processed
            var processed = 0;
            foreach (var file in files)
            {
                report?.Invoke(processed, files.Count, Path.GetFileName(file));
                SyntaxNode root;
                try { root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot(); }
                catch { processed++; continue; }
        
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
                    if (name == null) continue;
                    if (!index.BySimpleName.ContainsKey(name)) index.BySimpleName[name] = node;
                    var ns = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
                    if (ns.Length > 0) index.ByFullName.TryAdd(ns + "." + name, node);
                }
                processed++;
            }
            report?.Invoke(files.Count, files.Count, "");
            return index;
        }

        /// <summary>
        /// 类型索引：ByFullName 为 命名空间.类型名 精确键；BySimpleName 为简单名首现键。
        /// 同名类型在不同命名空间下（多项目各自的 BaseController）经 ByFullName 并存互不覆盖。
        /// </summary>
        private sealed class ApiTypeIndex
        {
            public Dictionary<string, SyntaxNode> ByFullName { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, SyntaxNode> BySimpleName { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// 解析基类声明：优先按 当前类所在命名空间.基类名 精确匹配（同命名空间基类，
        /// 如各项目自己的 BaseController）；其次按 using 命名空间组合匹配；最后回退简单名首现索引。
        /// </summary>
        private static bool TryResolveBaseClass(ClassDeclarationSyntax derived, string baseSimpleName,
            ApiTypeIndex index, out ClassDeclarationSyntax? baseCls)
        {
            baseCls = null;
            var ns = derived.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
            if (ns.Length > 0 && index.ByFullName.TryGetValue(ns + "." + baseSimpleName, out var node)
                && node is ClassDeclarationSyntax sameNs)
            {
                baseCls = sameNs;
                return true;
            }

            foreach (var u in EnumerateUsings(derived))
            {
                if (index.ByFullName.TryGetValue(u + "." + baseSimpleName, out node) && node is ClassDeclarationSyntax viaUsing)
                {
                    baseCls = viaUsing;
                    return true;
                }
            }

            if (index.BySimpleName.TryGetValue(baseSimpleName, out node) && node is ClassDeclarationSyntax fallback)
            {
                baseCls = fallback;
                return true;
            }
            return false;
        }

        /// <summary>收集文件级与块命名空间内的 using 命名空间（去重，保序）。</summary>
        private static IEnumerable<string> EnumerateUsings(SyntaxNode node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cu in node.Ancestors().OfType<CompilationUnitSyntax>())
                foreach (var u in cu.Usings)
                {
                    var name = u.Name?.ToString();
                    if (!string.IsNullOrEmpty(name) && seen.Add(name)) yield return name;
                }
            foreach (var n in node.Ancestors().OfType<NamespaceDeclarationSyntax>())
                foreach (var u in n.Usings)
                {
                    var name = u.Name?.ToString();
                    if (!string.IsNullOrEmpty(name) && seen.Add(name)) yield return name;
                }
        }

        /// <summary>
        /// 把类型文本里引用的自定义类型加入解析队列（泛型逐层拆出标识符，
        /// 基元/集合类型名忽略，已知类型跳过），随后逐个解析为 ApiSpecTypeDto。
        /// </summary>
        private static void CollectTypeNames(string typeText, ApiSpecDocumentDto doc, ApiTypeIndex typeIndex)
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

                // DTO 展开沿用简单名首现匹配（与历史行为一致）；路由基类解析才用命名空间精确匹配
                if (!typeIndex.BySimpleName.TryGetValue(name, out var decl))
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
