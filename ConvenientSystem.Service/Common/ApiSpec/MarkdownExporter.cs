using System.Text;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.ApiSpec
{
    /// <summary>
    /// Markdown 接口文档导出器：生成人读友好的接口文档（按 Controller 分节、
    /// 接口表 + 参数表 + DTO 字段表），适合放 Wiki / README。
    /// </summary>
    public class MarkdownExporter : IApiExporter
    {
        public string Format => "markdown";
        public string DisplayName => "Markdown 接口文档";
        public string FileExtension => ".md";
        public string ContentType => "text/markdown";
        public string Description => "人读接口文档，适合放 Wiki / README / 归档评审";
        public string FileNameBase => "api-spec.doc";

        public string Export(ApiSpecDocumentDto doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {doc.Title}");
            sb.AppendLine();
            sb.AppendLine($"> BaseUrl：`{doc.BaseUrl}`　|　版本：`{doc.Version}`　|　接口数：{doc.Endpoints.Count}");
            sb.AppendLine();
            sb.AppendLine("> 认证方式：`Authorization: Bearer <token>`（除标注匿名的接口外均需登录）");
            sb.AppendLine();

            // 目录
            sb.AppendLine("## 目录");
            sb.AppendLine();
            var anchor = 1;
            var anchors = new Dictionary<string, string>();
            foreach (var group in doc.Endpoints.GroupBy(e => e.Group))
            {
                var link = $"group-{anchor++}";
                anchors[group.Key] = link;
                sb.AppendLine($"{anchor - 1}. [{group.Key}](#{link})（{group.Count()} 个接口）");
            }
            sb.AppendLine($"{anchor}. [数据结构（DTO）](#dto-types)");
            sb.AppendLine();

            // 接口明细
            foreach (var group in doc.Endpoints.GroupBy(e => e.Group))
            {
                sb.AppendLine($"## {group.Key}");
                sb.AppendLine();
                foreach (var ep in group)
                {
                    sb.AppendLine($"### {ep.Method} `{ep.Path}`");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(ep.Summary)) sb.AppendLine(ep.Summary);
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(ep.Permission))
                        sb.AppendLine($"> **权限码：`{ep.Permission}`**（需在角色/用户权限中授权）");
                    if (!string.IsNullOrWhiteSpace(ep.ResponseType))
                        sb.AppendLine($"> **响应类型：`{ep.ResponseType}`**");
                    sb.AppendLine();

                    var queryParams = ep.Params.Where(p => p.In is "query" or "path" or "header").ToList();
                    if (queryParams.Count > 0)
                    {
                        sb.AppendLine("| 位置 | 参数 | 类型 | 必填 | 说明 |");
                        sb.AppendLine("|---|---|---|---|---|");
                        foreach (var p in queryParams)
                            sb.AppendLine($"| {p.In} | `{p.Name}` | `{p.TypeText}` | {(p.In == "path" || p.Required ? "是" : "否")} | {p.Description ?? ""} |");
                        sb.AppendLine();
                    }

                    var body = ep.Params.FirstOrDefault(p => p.In is "body" or "form");
                    if (body != null)
                    {
                        sb.AppendLine($"**请求体（{(body.In == "form" ? "multipart/form-data" : "application/json")}）**：`{body.TypeText}`");
                        var cleanType = body.TypeText.TrimEnd('?');
                        if (doc.Types.TryGetValue(cleanType, out var bodyType) && !bodyType.IsEnum)
                        {
                            sb.AppendLine();
                            sb.AppendLine("| 字段 | 类型 | 必填 | 说明 |");
                            sb.AppendLine("|---|---|---|---|");
                            foreach (var f in bodyType.Fields)
                                sb.AppendLine($"| `{f.Name}` | `{f.TypeText}` | {(f.Required ? "是" : "否")} | {f.Description ?? ""} |");
                        }
                        sb.AppendLine();
                    }
                }
            }

            // DTO 字典
            sb.AppendLine("## 数据结构（DTO）");
            sb.AppendLine();
            if (doc.Types.Count == 0)
            {
                sb.AppendLine("（本批接口未引用自定义 DTO）");
            }
            foreach (var (name, type) in doc.Types.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"### {(type.IsEnum ? "枚举" : "对象")}：`{name}`");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(type.Comment)) sb.AppendLine(type.Comment);
                sb.AppendLine();
                if (type.IsEnum)
                {
                    sb.AppendLine("```");
                    sb.AppendLine(string.Join(" | ", type.EnumValues));
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine("| 字段 | 类型 | 必填 | 说明 |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var f in type.Fields)
                        sb.AppendLine($"| `{f.Name}` | `{f.TypeText}` | {(f.Required ? "是" : "否")} | {f.Description ?? ""} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
