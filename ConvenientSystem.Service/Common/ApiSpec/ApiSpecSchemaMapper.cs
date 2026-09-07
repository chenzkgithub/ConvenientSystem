using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.ApiSpec
{
    /// <summary>
    /// 共享工具：C# 类型文本 → OpenAPI Schema 节点、类型 → 示例值、JsonNode → YAML 文本。
    /// 全部基于字符串启发式（无需 Roslyn 语义模型），覆盖本项目 DTO 常见的基元/集合/嵌套类型。
    /// </summary>
    public static class ApiSpecSchemaMapper
    {
        /// <summary>基元/已知类型 → OpenAPI schema（返回 null 表示非已知类型，应按 $ref 处理）。</summary>
        private static JsonObject? MapKnownType(string type)
        {
            switch (type)
            {
                case "int": case "short": case "byte": case "sbyte": case "ushort":
                    return new JsonObject { ["type"] = "integer", ["format"] = "int32" };
                case "long": case "ulong":
                    return new JsonObject { ["type"] = "integer", ["format"] = "int64" };
                case "decimal": case "double": case "float":
                    return new JsonObject { ["type"] = "number" };
                case "bool":
                    return new JsonObject { ["type"] = "boolean" };
                case "string": case "char":
                    return new JsonObject { ["type"] = "string" };
                case "DateTime": case "DateTimeOffset":
                    return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
                case "DateOnly":
                    return new JsonObject { ["type"] = "string", ["format"] = "date" };
                case "TimeOnly": case "TimeSpan":
                    return new JsonObject { ["type"] = "string" };
                case "Guid":
                    return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
                case "object": case "dynamic": case "JsonElement": case "JsonNode": case "JsonDocument":
                    return new JsonObject { ["type"] = "object" };
                case "IFormFile":
                    return new JsonObject { ["type"] = "string", ["format"] = "binary" };
                case "byte[]":
                    return new JsonObject { ["type"] = "string", ["format"] = "byte" };
            }
            return null;
        }

        /// <summary>集合泛型类型名（T 的集合形式 → T）。</summary>
        private static readonly string[] CollectionTypes =
        {
            "List", "IList", "IEnumerable", "ICollection", "IReadOnlyList", "IReadOnlyCollection", "HashSet", "Queue", "Stack", "LinkedList"
        };

        /// <summary>
        /// C# 类型文本 → OpenAPI schema 节点（含 array/dictionary/嵌套 $ref 递归）。
        /// </summary>
        /// <param name="typeText">类型文本（如 List&lt;NoticeDto&gt;、int?、Dictionary&lt;string,int&gt;）。</param>
        /// <param name="doc">IR 文档（查枚举类型生成 enum schema）。</param>
        public static JsonObject MapType(string typeText, ApiSpecDocumentDto doc)
        {
            var t = (typeText ?? "").Trim();
            var nullable = false;
            if (t.EndsWith("?")) { nullable = true; t = t[..^1].Trim(); }

            // Nullable<T> 解包
            if (t.StartsWith("Nullable<") && t.EndsWith(">"))
            {
                nullable = true;
                t = t[9..^1].Trim();
            }

            JsonObject schema;
            if (t.EndsWith("[]"))
            {
                // T[] 数组
                schema = new JsonObject { ["type"] = "array", ["items"] = MapType(t[..^2], doc) };
            }
            else if (TryParseGeneric(t, out var genericName, out var genericArgs))
            {
                if (CollectionTypes.Contains(genericName) && genericArgs.Count == 1)
                {
                    schema = new JsonObject { ["type"] = "array", ["items"] = MapType(genericArgs[0], doc) };
                }
                else if (genericName == "Dictionary" && genericArgs.Count == 2
                         && (genericArgs[0] == "string" || genericArgs[0] == "int" || genericArgs[0] == "long"))
                {
                    // Dictionary<string, T> → object + additionalProperties
                    schema = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = MapType(genericArgs[1], doc),
                    };
                }
                else if (genericName == "KeyValuePair" && genericArgs.Count == 2)
                {
                    schema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["key"] = MapType(genericArgs[0], doc),
                            ["value"] = MapType(genericArgs[1], doc),
                        },
                    };
                }
                else if (genericName == "Task" && genericArgs.Count == 1)
                {
                    return MapType(genericArgs[0], doc);
                }
                else if (genericName == "ActionResult" && genericArgs.Count == 1)
                {
                    return MapType(genericArgs[0], doc);
                }
                else
                {
                    // 未识别的泛型 → object 兜底
                    schema = new JsonObject { ["type"] = "object" };
                }
            }
            else if (doc.Types.TryGetValue(t, out var typeDef) && typeDef.IsEnum)
            {
                // 枚举类型：string + enum 值列表
                var enumArray = new JsonArray();
                foreach (var v in typeDef.EnumValues) enumArray.Add(JsonValue.Create(v));
                schema = new JsonObject { ["type"] = "string", ["enum"] = enumArray };
            }
            else if (MapKnownType(t) is JsonObject known)
            {
                schema = known;
            }
            else if (doc.Types.ContainsKey(t))
            {
                // 用户 DTO → $ref
                schema = new JsonObject { ["$ref"] = $"#/components/schemas/{t}" };
            }
            else
            {
                // 类型未解析（未在扫描范围内）→ object 兜底
                schema = new JsonObject { ["type"] = "object" };
            }

            if (nullable) schema["nullable"] = true;
            return schema;
        }

        /// <summary>类型文本是否为用户 DTO 引用（决定 requestBody/响应是否展开）。</summary>
        public static bool IsDtoRef(string typeText, ApiSpecDocumentDto doc)
        {
            var t = (typeText ?? "").Trim().TrimEnd('?');
            return doc.Types.ContainsKey(t) && !doc.Types[t].IsEnum;
        }

        /// <summary>
        /// 类型文本 → 示例值 JsonNode（Postman 请求体 / OpenAPI example 用）。
        /// DTO 引用递归展开，深度超过 maxDepth 时用空对象截断。
        /// </summary>
        public static JsonNode? BuildExample(string typeText, ApiSpecDocumentDto doc, int maxDepth = 4)
        {
            var t = (typeText ?? "").Trim().TrimEnd('?');
            if (t.Length == 0) return null;
            if (maxDepth <= 0) return new JsonObject();

            if (t.EndsWith("[]"))
                return new JsonArray { BuildExample(t[..^2], doc, maxDepth - 1) ?? JsonValue.Create("string") };

            if (TryParseGeneric(t, out var genericName, out var genericArgs))
            {
                if (CollectionTypes.Contains(genericName) && genericArgs.Count == 1)
                    return new JsonArray { BuildExample(genericArgs[0], doc, maxDepth - 1) ?? JsonValue.Create("string") };
                if (genericName == "Dictionary" && genericArgs.Count == 2)
                    return new JsonObject { ["key"] = BuildExample(genericArgs[1], doc, maxDepth - 1) ?? JsonValue.Create("string") };
                if (genericName == "Task" || genericName == "ActionResult")
                    return genericArgs.Count == 1 ? BuildExample(genericArgs[0], doc, maxDepth - 1) : null;
                return new JsonObject();
            }

            if (doc.Types.TryGetValue(t, out var typeDef))
            {
                if (typeDef.IsEnum)
                    return typeDef.EnumValues.Count > 0 ? JsonValue.Create(typeDef.EnumValues[0]) : JsonValue.Create("string");
                var obj = new JsonObject();
                foreach (var f in typeDef.Fields)
                    obj[f.Name] = BuildExample(f.TypeText, doc, maxDepth - 1) ?? JsonValue.Create("string");
                return obj;
            }

            return t switch
            {
                "int" or "short" or "byte" or "sbyte" or "ushort" => JsonValue.Create(0),
                "long" or "ulong" => JsonValue.Create(0),
                "decimal" or "double" or "float" => JsonValue.Create(0),
                "bool" => JsonValue.Create(true),
                "DateTime" or "DateTimeOffset" => JsonValue.Create("2026-01-01T00:00:00Z"),
                "DateOnly" => JsonValue.Create("2026-01-01"),
                "Guid" => JsonValue.Create("00000000-0000-0000-0000-000000000000"),
                "char" => JsonValue.Create("c"),
                "IFormFile" => JsonValue.Create("(binary)"),
                "byte[]" => JsonValue.Create(""),
                _ => JsonValue.Create(t == "string" ? "string" : "string"),
            };
        }

        /// <summary>解析泛型文本：List&lt;T&gt; → ("List", [T])；Dictionary&lt;string,T&gt; 正确处理嵌套尖括号。</summary>
        public static bool TryParseGeneric(string typeText, out string genericName, out List<string> genericArgs)
        {
            genericName = "";
            genericArgs = new List<string>();
            var lt = typeText.IndexOf('<');
            if (lt <= 0 || !typeText.EndsWith(">")) return false;

            genericName = typeText[..lt].Trim();
            var inner = typeText[(lt + 1)..^1];

            // 按顶层逗号切分（深度为 0 的逗号）
            var depth = 0;
            var start = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                switch (inner[i])
                {
                    case '<': depth++; break;
                    case '>': depth--; break;
                    case ',' when depth == 0:
                        genericArgs.Add(inner[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }
            genericArgs.Add(inner[start..].Trim());
            return genericArgs.Count > 0;
        }

        // ========== JsonNode → YAML ==========

        /// <summary>
        /// JsonNode → YAML 文本（2 空格缩进、字符串值一律双引号转义，规避特殊字符歧义；
        /// 用于 OpenAPI YAML 输出，文档结构无锚点/多行标量需求，子集序列化足够）。
        /// </summary>
        public static string ToYaml(JsonNode? node)
        {
            var sb = new StringBuilder();
            WriteYaml(sb, node, 0);
            return sb.ToString();
        }

        private static void WriteYaml(StringBuilder sb, JsonNode? node, int indent)
        {
            var pad = new string(' ', indent * 2);
            switch (node)
            {
                case JsonObject obj:
                    if (obj.Count == 0) { sb.AppendLine($"{pad}{{}}"); break; }
                    foreach (var kv in obj)
                    {
                        if (kv.Value is JsonObject childObj && childObj.Count > 0)
                        {
                            sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}:");
                            WriteYaml(sb, childObj, indent + 1);
                        }
                        else if (kv.Value is JsonArray childArr && childArr.Count > 0)
                        {
                            sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}:");
                            WriteYaml(sb, childArr, indent + 1);
                        }
                        else
                        {
                            sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}: {ScalarYaml(kv.Value)}");
                        }
                    }
                    break;

                case JsonArray arr:
                    if (arr.Count == 0) { sb.AppendLine($"{pad}[]"); break; }
                    foreach (var item in arr)
                    {
                        if (item is JsonObject objItem && objItem.Count > 0)
                        {
                            // 数组首键与 "- " 同行，其余键对齐
                            sb.Append($"{pad}- ");
                            WriteYamlFirstOnLine(sb, objItem, indent + 1);
                        }
                        else if (item is JsonArray arrItem && arrItem.Count > 0)
                        {
                            sb.AppendLine($"{pad}- ");
                            WriteYaml(sb, arrItem, indent + 1);
                        }
                        else
                        {
                            sb.AppendLine($"{pad}- {ScalarYaml(item)}");
                        }
                    }
                    break;

                default:
                    sb.AppendLine($"{pad}{ScalarYaml(node)}");
                    break;
            }
        }

        /// <summary>数组对象首键同行写法（"- key: value" 其余键换行对齐）。</summary>
        private static void WriteYamlFirstOnLine(StringBuilder sb, JsonObject obj, int indent)
        {
            var pad = new string(' ', indent * 2);
            var first = true;
            foreach (var kv in obj)
            {
                if (first)
                {
                    if (kv.Value is JsonObject childObj && childObj.Count > 0)
                    {
                        sb.AppendLine($"{QuoteYamlKey(kv.Key)}:");
                        WriteYaml(sb, childObj, indent + 1);
                    }
                    else if (kv.Value is JsonArray childArr && childArr.Count > 0)
                    {
                        sb.AppendLine($"{QuoteYamlKey(kv.Key)}:");
                        WriteYaml(sb, childArr, indent + 1);
                    }
                    else
                    {
                        sb.AppendLine($"{QuoteYamlKey(kv.Key)}: {ScalarYaml(kv.Value)}");
                    }
                    first = false;
                }
                else
                {
                    if (kv.Value is JsonObject childObj && childObj.Count > 0)
                    {
                        sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}:");
                        WriteYaml(sb, childObj, indent + 1);
                    }
                    else if (kv.Value is JsonArray childArr && childArr.Count > 0)
                    {
                        sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}:");
                        WriteYaml(sb, childArr, indent + 1);
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{QuoteYamlKey(kv.Key)}: {ScalarYaml(kv.Value)}");
                    }
                }
            }
        }

        /// <summary>标量值序列化：null/true/false/数字原样，字符串双引号转义。</summary>
        private static string ScalarYaml(JsonNode? node) => node switch
        {
            null => "null",
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
            JsonValue v when v.TryGetValue<decimal>(out var d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            JsonValue v when v.TryGetValue<long>(out var l) => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValue v when v.TryGetValue<int>(out var i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => node!.ToJsonString(JsonOptions),  // JsonValue(string) 的 ToJsonString 返回带双引号+完整转义的 JSON 字符串，与 YAML 双引号标量语法兼容
        };

        /// <summary>YAML 键：含特殊字符时加双引号（复用 JSON 序列化转义），否则裸写。</summary>
        private static string QuoteYamlKey(string key)
        {
            if (key.Length == 0) return "\"\"";
            var special = key[0] is '&' or '*' or '!' or '%' or '@' or '`' or '-' or '?' or '#' or ',' or '[' or ']' or '{' or '}' or '|';
            if (special || key.Contains(':') || key.Contains(' ') || key.Contains('#') || key.Contains('"') || key.Contains('\''))
                return JsonSerializer.Serialize(key, JsonOptions);
            return key;
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    }
}
