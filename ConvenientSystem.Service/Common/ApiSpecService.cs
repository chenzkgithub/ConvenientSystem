using System.Text.RegularExpressions;
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

        public ApiSpecDocumentDto Parse(string rootDir, string files, string? title, string? baseUrl)
        {
            var dir = ValidateRoot(rootDir);
            var selected = SplitFiles(files);
            if (selected.Count == 0) throw new BizException("未选择任何 Controller 文件");

            // 全项目类型索引：类型名 → 语法声明（跨文件解析 DTO 字段树的关键）
            var typeIndex = BuildTypeIndex(dir);

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
                    ParseController(doc, typeIndex, group.Key, group);
                }
            }

            if (doc.Endpoints.Count == 0) throw new BizException("选中文件里未解析到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）");

            doc.Endpoints = doc.Endpoints
                .OrderBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Method).ToList();
            return doc;
        }

        public ApiSpecExportDto Export(string rootDir, string files, string format, string? title, string? baseUrl)
        {
            var exporter = _exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
                ?? throw new BizException($"不支持的导出格式：{format}");
            var doc = Parse(rootDir, files, title, baseUrl);
            return new ApiSpecExportDto
            {
                FileName = exporter.FileNameBase + exporter.FileExtension,
                ContentType = exporter.ContentType,
                Content = exporter.Export(doc),
                Warnings = doc.Warnings,
            };
        }

        // ========== Controller / Action 解析 ==========

        private static void ParseController(ApiSpecDocumentDto doc, Dictionary<string, SyntaxNode> typeIndex, string className, IEnumerable<ClassDeclarationSyntax> declarations)
        {
            var groupName = className;
            var routePrefix = "";
            var controllerToken = className[..^"Controller".Length];

            foreach (var cls in declarations)
            {
                // 类级 [Route("...")]（取第一个）
                if (routePrefix.Length == 0)
                {
                    var routeAttr = GetAttributes(cls).FirstOrDefault(a => a.Name.ToString() == "Route");
                    routePrefix = GetLiteral(routeAttr) ?? "";
                }

                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(m => m.ValueText == "public")) continue;

                    var httpAttr = GetAttributes(method).FirstOrDefault(a => HttpAttrNames.Contains(a.Name.ToString()));
                    if (httpAttr == null) continue;

                    var verb = httpAttr.Name.ToString()["Http".Length..].ToUpperInvariant();
                    var methodTemplate = GetLiteral(httpAttr) ?? "";
                    var fullPath = CombineRoute(routePrefix, methodTemplate, controllerToken);

                    var ep = new ApiSpecEndpointDto
                    {
                        Method = verb,
                        ActionName = method.Identifier.ValueText,
                        Path = fullPath,
                        Summary = ExtractSummary(method),
                        Permission = GetPermission(method),
                        Group = groupName,
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
        /// 方法模板以 / 或 ~ 开头时忽略类前缀；[controller] 替换为类名去 Controller 后缀；[action] 替换方法名。
        /// </summary>
        private static string CombineRoute(string routePrefix, string methodTemplate, string controllerToken)
        {
            routePrefix = routePrefix.Replace("[controller]", controllerToken);
            methodTemplate = methodTemplate.Replace("[controller]", controllerToken);

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
        {
            var index = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
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
                    doc.Warnings.Add($"类型 {name} 未在扫描范围内找到，已按 object 处理");
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
