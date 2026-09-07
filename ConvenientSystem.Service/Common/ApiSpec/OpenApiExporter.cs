using System.Text.Json;
using System.Text.Json.Nodes;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.ApiSpec
{
    /// <summary>
    /// OpenAPI 3.0 导出器基类：构建 OpenAPI 文档 JsonObject（JSON / YAML 两个子类只差最终序列化）。
    /// OpenAPI 是事实标准枢纽，Apifox / Postman / Insomnia / Swagger UI 等工具均可导入。
    /// </summary>
    public abstract class OpenApiExporterBase : IApiExporter
    {
        public abstract string Format { get; }
        public abstract string DisplayName { get; }
        public abstract string FileExtension { get; }
        public abstract string ContentType { get; }
        public abstract string Description { get; }
        public virtual string FileNameBase => "api-spec.openapi3";

        public string Export(ApiSpecDocumentDto doc)
        {
            var root = BuildDocument(doc);
            return Serialize(root);
        }

        /// <summary>子类实现最终序列化（JSON 缩进 / YAML）。</summary>
        protected abstract string Serialize(JsonObject root);

        private static JsonObject BuildDocument(ApiSpecDocumentDto doc)
        {
            var root = new JsonObject
            {
                ["openapi"] = "3.0.3",
                ["info"] = new JsonObject
                {
                    ["title"] = doc.Title,
                    ["version"] = doc.Version,
                    ["description"] = $"由 ConvenientSystem API 文档生成器从 C# Controller 源码生成，共 {doc.Endpoints.Count} 个接口。",
                },
                ["servers"] = new JsonArray { new JsonObject { ["url"] = doc.BaseUrl } },
            };

            // tags：按 Controller 分组
            var tags = new JsonArray();
            foreach (var group in doc.Endpoints.Select(e => e.Group).Distinct())
                tags.Add(new JsonObject { ["name"] = group, ["description"] = $"{group} 接口集合" });
            root["tags"] = tags;

            // paths：同一路径下合并多个 HTTP 方法
            var paths = new JsonObject();
            foreach (var ep in doc.Endpoints)
            {
                if (paths[ep.Path] is not JsonObject pathItem)
                {
                    pathItem = new JsonObject();
                    paths[ep.Path] = pathItem;
                }

                var operation = new JsonObject
                {
                    ["tags"] = new JsonArray { JsonValue.Create(ep.Group) },
                    ["summary"] = ep.Summary,
                    ["operationId"] = $"{ep.Group}_{ep.ActionName}",
                };
                var descParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ep.Permission)) descParts.Add($"权限码：{ep.Permission}");
                if (descParts.Count > 0) operation["description"] = string.Join("；", descParts);

                // parameters（path/query/header）与 requestBody（body/form）
                var parameters = new JsonArray();
                foreach (var p in ep.Params)
                {
                    if (p.In is "path" or "query" or "header")
                    {
                        parameters.Add(new JsonObject
                        {
                            ["name"] = p.Name,
                            ["in"] = p.In,
                            ["required"] = p.In == "path" || p.Required,
                            ["description"] = p.Description,
                            ["schema"] = ApiSpecSchemaMapper.MapType(p.TypeText, doc),
                        });
                    }
                    else if (p.In is "body" or "form")
                    {
                        var isForm = p.In == "form";
                        var contentType = isForm ? "multipart/form-data" : "application/json";
                        operation["requestBody"] = new JsonObject
                        {
                            ["required"] = true,
                            ["content"] = new JsonObject
                            {
                                [contentType] = new JsonObject
                                {
                                    ["schema"] = ApiSpecSchemaMapper.MapType(p.TypeText, doc),
                                },
                            },
                        };
                    }
                }
                if (parameters.Count > 0) operation["parameters"] = parameters;

                // responses
                var okResponse = new JsonObject { ["description"] = "成功" };
                if (!string.IsNullOrWhiteSpace(ep.ResponseType))
                {
                    okResponse["content"] = new JsonObject
                    {
                        ["application/json"] = new JsonObject
                        {
                            ["schema"] = ApiSpecSchemaMapper.MapType(ep.ResponseType, doc),
                            ["example"] = ApiSpecSchemaMapper.BuildExample(ep.ResponseType, doc),
                        },
                    };
                }
                operation["responses"] = new JsonObject
                {
                    ["200"] = okResponse,
                    ["401"] = new JsonObject { ["description"] = "未登录或登录已过期" },
                    ["403"] = new JsonObject { ["description"] = "无访问权限" },
                };

                pathItem[ep.Method.ToLowerInvariant()] = operation;
            }
            root["paths"] = paths;

            // components.schemas：全部引用到的 DTO 类型
            var schemas = new JsonObject();
            foreach (var (name, type) in doc.Types.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (type.IsEnum)
                {
                    var enumArray = new JsonArray();
                    foreach (var v in type.EnumValues) enumArray.Add(JsonValue.Create(v));
                    schemas[name] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = enumArray,
                        ["description"] = type.Comment,
                    };
                    continue;
                }

                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var f in type.Fields)
                {
                    var prop = ApiSpecSchemaMapper.MapType(f.TypeText, doc);
                    if (!string.IsNullOrWhiteSpace(f.Description)) prop["description"] = f.Description;
                    properties[f.Name] = prop;
                    if (f.Required) required.Add(JsonValue.Create(f.Name));
                }
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                };
                if (!string.IsNullOrWhiteSpace(type.Comment)) schema["description"] = type.Comment;
                if (required.Count > 0) schema["required"] = required;
                schemas[name] = schema;
            }
            root["components"] = new JsonObject { ["schemas"] = schemas };
            return root;
        }
    }

    /// <summary>OpenAPI 3.0 JSON 导出器（缩进 2 空格）。</summary>
    public class OpenApiJsonExporter : OpenApiExporterBase
    {
        public override string Format => "openapi3-json";
        public override string DisplayName => "OpenAPI 3.0 (JSON)";
        public override string FileExtension => ".json";
        public override string ContentType => "application/json";
        public override string Description => "通用 API 描述标准，可导入 Apifox、Postman、Insomnia、Swagger UI 等几乎所有工具";

        private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

        protected override string Serialize(JsonObject root)
            => root.ToJsonString(PrettyOptions);
    }

    /// <summary>OpenAPI 3.0 YAML 导出器（JsonNode → YAML 子集序列化）。</summary>
    public class OpenApiYamlExporter : OpenApiExporterBase
    {
        public override string Format => "openapi3-yaml";
        public override string DisplayName => "OpenAPI 3.0 (YAML)";
        public override string FileExtension => ".yaml";
        public override string ContentType => "application/yaml";
        public override string Description => "OpenAPI 的 YAML 形态，适合版本库存储与人工阅读（工具兼容性同 JSON 版）";

        protected override string Serialize(JsonObject root)
            => ApiSpecSchemaMapper.ToYaml(root);
    }
}
