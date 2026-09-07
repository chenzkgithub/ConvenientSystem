using System.Text.Json;
using System.Text.Json.Nodes;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.ApiSpec
{
    /// <summary>
    /// Postman Collection v2.1 导出器：生成可直接导入 Postman / Apifox 的集合文件。
    /// 请求体带 JSON 示例（从 DTO 类型树生成），导入后可直接发送调试。
    /// baseUrl 写为集合变量 {{baseUrl}}，导入后可在 Postman 环境里统一改。
    /// </summary>
    public class PostmanExporter : IApiExporter
    {
        public string Format => "postman";
        public string DisplayName => "Postman Collection v2.1";
        public string FileExtension => ".json";
        public string ContentType => "application/json";
        public string Description => "Postman 官方集合格式，可导入 Postman / Apifox，请求体自动带示例值";
        public string FileNameBase => "api-spec.postman";

        private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

        public string Export(ApiSpecDocumentDto doc)
        {
            var root = new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["name"] = doc.Title,
                    ["_postman_id"] = Guid.NewGuid().ToString("D"),
                    ["description"] = $"由 ConvenientSystem API 文档生成器从 C# Controller 源码生成，共 {doc.Endpoints.Count} 个接口。服务器地址请修改集合变量 baseUrl。",
                    ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
                },
                ["variable"] = new JsonArray
                {
                    new JsonObject { ["key"] = "baseUrl", ["value"] = doc.BaseUrl, ["type"] = "string" },
                },
            };

            // item：按 Controller 分组建 folder
            var items = new JsonArray();
            foreach (var group in doc.Endpoints.GroupBy(e => e.Group))
            {
                var folderItems = new JsonArray();
                foreach (var ep in group)
                    folderItems.Add(BuildRequestItem(ep, doc));

                items.Add(new JsonObject
                {
                    ["name"] = group.Key,
                    ["description"] = $"{group.Key} 接口集合",
                    ["item"] = folderItems,
                });
            }
            root["item"] = items;

            return root.ToJsonString(PrettyOptions);
        }

        private static JsonObject BuildRequestItem(ApiSpecEndpointDto ep, ApiSpecDocumentDto doc)
        {
            // URL：path 段 + query 参数；path 模板 {id} 由 Postman 自动识别为路径变量
            var pathValue = ep.Path.StartsWith('/') ? ep.Path : "/" + ep.Path;
            var segments = pathValue.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var host = new JsonArray { JsonValue.Create("{{baseUrl}}") };
            var pathSegments = new JsonArray();
            foreach (var s in segments) pathSegments.Add(JsonValue.Create(s));

            var query = new JsonArray();
            var bodyParam = ep.Params.FirstOrDefault(p => p.In is "body" or "form");
            foreach (var p in ep.Params.Where(p => p.In == "query"))
            {
                query.Add(new JsonObject
                {
                    ["key"] = p.Name,
                    ["value"] = ExampleScalar(p.TypeText, doc),
                    ["description"] = string.IsNullOrWhiteSpace(p.Description)
                        ? (p.Required ? "必填" : "可选")
                        : p.Description + (p.Required ? "（必填）" : "（可选）"),
                });
            }

            var request = new JsonObject
            {
                ["method"] = ep.Method,
                ["header"] = new JsonArray
                {
                    new JsonObject { ["key"] = "Authorization", ["value"] = "Bearer {{token}}", ["type"] = "text" },
                },
                ["url"] = new JsonObject
                {
                    ["raw"] = "{{baseUrl}}" + pathValue + BuildQueryString(query),
                    ["host"] = host,
                    ["path"] = pathSegments,
                },
                ["description"] = BuildDescription(ep),
            };
            if (query.Count > 0) ((JsonObject)request["url"]!)["query"] = query;

            // 请求体：body 参数生成 JSON 示例
            if (bodyParam != null && bodyParam.In == "body")
            {
                var example = ApiSpecSchemaMapper.BuildExample(bodyParam.TypeText, doc);
                request["body"] = new JsonObject
                {
                    ["mode"] = "raw",
                    ["raw"] = example?.ToJsonString(PrettyOptions) ?? "{}",
                    ["options"] = new JsonObject
                    {
                        ["raw"] = new JsonObject { ["language"] = "json" },
                    },
                };
            }
            else if (bodyParam != null && bodyParam.In == "form")
            {
                // form（含 IFormFile）：生成 formdata 占位
                var formRows = new JsonArray();
                foreach (var f in doc.Types.TryGetValue(bodyParam.TypeText.TrimEnd('?'), out var t) && !t.IsEnum ? t.Fields : new List<ApiSpecFieldDto>())
                {
                    formRows.Add(new JsonObject
                    {
                        ["key"] = f.Name,
                        ["value"] = ExampleScalar(f.TypeText, doc),
                        ["type"] = f.TypeText.Contains("IFormFile") ? "file" : "text",
                    });
                }
                request["body"] = new JsonObject { ["mode"] = "formdata", ["formdata"] = formRows };
            }

            return new JsonObject
            {
                ["name"] = $"{ep.Method} {ep.Path}",
                ["request"] = request,
                ["response"] = new JsonArray(),
            };
        }

        private static string BuildDescription(ApiSpecEndpointDto ep)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(ep.Summary)) lines.Add(ep.Summary);
            if (!string.IsNullOrWhiteSpace(ep.Permission)) lines.Add($"**权限码：`{ep.Permission}`**");
            if (!string.IsNullOrWhiteSpace(ep.ResponseType)) lines.Add($"**响应类型：** `{ep.ResponseType}`");
            return string.Join("\n\n", lines);
        }

        /// <summary>query 参数的示例标量（从类型推断，DTO 引用给占位 JSON）。</summary>
        private static string ExampleScalar(string typeText, ApiSpecDocumentDto doc)
        {
            var example = ApiSpecSchemaMapper.BuildExample(typeText, doc, maxDepth: 1);
            if (example is JsonValue v)
            {
                return v.TryGetValue<string>(out var s) ? s : example.ToJsonString();
            }
            return example?.ToJsonString() ?? "";
        }

        /// <summary>拼 query 串（示例值非空才拼，避免生成一堆空 ?a=&b=）。</summary>
        private static string BuildQueryString(JsonArray query)
        {
            if (query.Count == 0) return "";
            var sb = new System.Text.StringBuilder("?");
            var first = true;
            foreach (var q in query)
            {
                var obj = (JsonObject)q!;
                if (!first) sb.Append('&');
                sb.Append(obj["key"]!.GetValue<string>());
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(obj["value"]?.GetValue<string>() ?? ""));
                first = false;
            }
            return sb.ToString();
        }
    }
}
