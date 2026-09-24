using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.DataProtection;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Apifox OpenAPI 导入与批量删除实现。
    /// 导入配置保存到当前用户的 UserConfig；Access Token 通过 ASP.NET Core Data Protection 加密后才落库。
    /// 导入与删除均为后台任务（统一 AsyncTaskCenter 进度表）：导入按 tag（命名空间/Controller）拆批逐次调用 Apifox；
    /// 删除受控并发执行（Apifox 无批量删除端点，逐个请求 + 瞬态失败退避重试），
    /// 进度经 AsyncTaskProgress 实时推送（轮询 GetTaskProgress 兜底）。
    /// </summary>
    public sealed class ApifoxImportService : IApifoxImportService
    {
        private const string TokenKey = "ApiSpec.Apifox.AccessToken";
        private const string ApiVersion = "2024-03-28";
        private const string SchemaRefPrefix = "#/components/schemas/";
        /// <summary>分页拉取接口列表的每页条数（Apifox 上限 500；列表只取 id/方法/路径/目录，直接拉满减少请求次数）。</summary>
        private const int ListPerPage = 500;
        /// <summary>分页拉取的防死循环上限（10 万接口）。</summary>
        private const int MaxListPages = 1000;
        /// <summary>删除阶段的受控并发数：Apifox 无批量删除端点，逐个请求但并行执行，过高易触发限流。</summary>
        private const int DeleteConcurrency = 5;
        /// <summary>删除请求瞬态失败（429 限流 / 502-504 网关 / 网络异常）的重试次数上限。</summary>
        private const int DeleteMaxRetries = 3;
        /// <summary>重试退避基数：第 n 次重试前等待 n × 基数（800ms / 1600ms / 2400ms）。</summary>
        private const int DeleteRetryDelayMs = 800;

        private static readonly HashSet<string> OverwriteBehaviors = new(StringComparer.Ordinal)
        {
            "OVERWRITE_EXISTING", "AUTO_MERGE", "KEEP_EXISTING", "CREATE_NEW",
        };

        /// <summary>删除请求可重试的 HTTP 状态码：429 限流与 502/503/504 网关瞬态错误。</summary>
        private static readonly HashSet<int> RetryableStatusCodes = new() { 429, 502, 503, 504 };

        /// <summary>OpenAPI path item 中属于 HTTP 方法的属性名，其余（summary/parameters 等）不属于接口分组。</summary>
        private static readonly HashSet<string> HttpMethodNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "get", "put", "post", "delete", "options", "head", "patch", "trace",
        };

        private static readonly JsonSerializerOptions PayloadJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IUserConfigService _userConfigService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _tokenProtector;
        private readonly ILogger<ApifoxImportService> _logger;
        private readonly AsyncTaskCenter _center;

        public ApifoxImportService(
            IUserConfigService userConfigService,
            IHttpClientFactory httpClientFactory,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<ApifoxImportService> logger,
            AsyncTaskCenter center)
        {
            _userConfigService = userConfigService;
            _httpClientFactory = httpClientFactory;
            _tokenProtector = dataProtectionProvider.CreateProtector("ConvenientSystem.ApiSpec.Apifox.AccessToken.v1");
            _logger = logger;
            _center = center;
        }

        public ApifoxAccessTokenStatusDto GetMyAccessTokenStatus()
            => new()
            {
                TokenConfigured = !string.IsNullOrWhiteSpace(_userConfigService.GetRawValue(TokenKey)),
            };

        public void SaveMyAccessToken(ApifoxAccessTokenSaveRequest request)
        {
            var oldToken = _userConfigService.GetRawValue(TokenKey);
            var newToken = request.AccessToken?.Trim();

            if (!string.IsNullOrEmpty(newToken) && newToken.Length > 2048)
                throw new BizException("Apifox Access Token 长度不合法");
            if (request.ClearAccessToken && !string.IsNullOrEmpty(newToken))
                throw new BizException("不能同时填写和清除 Access Token");
            if (!request.ClearAccessToken && string.IsNullOrEmpty(newToken) && string.IsNullOrWhiteSpace(oldToken))
                throw new BizException("请填写 Apifox Access Token");

            if (request.ClearAccessToken)
                _userConfigService.SetRawValue(TokenKey, string.Empty);
            else if (!string.IsNullOrEmpty(newToken))
                _userConfigService.SetRawValue(TokenKey, _tokenProtector.Protect(newToken));
        }

        /// <summary>查看当前用户已保存的 Access Token 明文：验证登录密码后解密返回；未配置或密码错误返回 null。</summary>
        public string? RevealMyAccessToken(string password)
        {
            if (!_userConfigService.VerifyLoginPassword(password ?? string.Empty)) return null;
            var protectedToken = _userConfigService.GetRawValue(TokenKey);
            if (string.IsNullOrWhiteSpace(protectedToken)) return null;
            return UnprotectAccessToken(protectedToken);
        }

        public AsyncTaskDto StartImport(ApifoxImportRequest request, Guid userId)
        {
            ValidateOpenApiContent(request?.Content);
            var projectId = ValidateProjectId(request!.ProjectId);
            var overwriteBehavior = NormalizeOverwriteBehavior(request.EndpointOverwriteBehavior);
            var endpointFolderId = ValidateOptionalId(request.TargetEndpointFolderId, "接口目录 ID");
            var branchId = ValidateOptionalId(request.TargetBranchId, "分支 ID");
            var accessToken = ResolveAccessToken();

            var handle = _center.Begin(userId, "apifox-import", "OpenAPI 导入 Apifox");
            _ = Task.Run(() => RunImportAsync(handle, projectId, endpointFolderId, branchId, overwriteBehavior,
                request.Content, accessToken));
            return _center.GetTask(handle.TaskId, userId)!;
        }

        public AsyncTaskDto StartDelete(ApifoxDeleteRequest request, Guid userId)
        {
            var projectId = ValidateProjectId(request?.ProjectId);
            var folderId = ValidateOptionalId(request!.FolderId, "接口目录 ID");
            var accessToken = ResolveAccessToken();

            var handle = _center.Begin(userId, "apifox-delete", "Apifox 批量删除");
            _ = Task.Run(() => RunDeleteAsync(handle, projectId, folderId, accessToken));
            return _center.GetTask(handle.TaskId, userId)!;
        }

        public bool CancelRunning(string? kind, Guid userId)
        {
            var normalized = kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized != "apifox-import" && normalized != "apifox-delete")
                throw new BadRequestException("仅支持终止 apifox-import / apifox-delete 类型的任务");
            return _center.CancelRunning(userId, normalized);
        }

        /// <summary>查询任务进度；任务不存在、已过期（完成超 30 分钟）或非本人任务时返回 null。</summary>
        public AsyncTaskDto? GetTaskProgress(Guid taskId, Guid userId)
            => _center.GetTask(taskId, userId);

        /// <summary>读取已完成任务的完整 Result（轮询快照不带 Result，终态后页面按需拉取）；
        /// 任务不存在、未完成或已过期时返回 null。</summary>
        public AsyncTaskDto? GetTaskResult(Guid taskId, Guid userId)
            => _center.GetResult(taskId, userId);

        // ==================== 导入任务 ====================

        private async Task RunImportAsync(AsyncTaskCenter.AsyncTaskHandle handle, string projectId, long? endpointFolderId,
            long? branchId, string overwriteBehavior, string content, string accessToken)
        {
            try
            {
                _center.Update(handle, p =>
                {
                    p.Phase = "preparing";
                    p.Current = "正在按分组拆分导入批次...";
                });

                var batches = SplitOpenApiByTag(content);

                _center.Update(handle, p =>
                {
                    p.Total = batches.Count;
                    p.Phase = "importing";
                });

                var aggregated = new ApifoxImportResultDto();
                foreach (var (tag, batchContent) in batches)
                {
                    _center.Update(handle, p => p.Current = $"正在导入分组：{tag}");
                    try
                    {
                        var result = await ImportSingleAsync(batchContent, projectId, endpointFolderId,
                            branchId, overwriteBehavior, accessToken);
                        aggregated.EndpointCreated += result.EndpointCreated;
                        aggregated.EndpointUpdated += result.EndpointUpdated;
                        aggregated.EndpointFailed += result.EndpointFailed;
                        aggregated.EndpointIgnored += result.EndpointIgnored;
                        aggregated.SchemaCreated += result.SchemaCreated;
                        aggregated.SchemaUpdated += result.SchemaUpdated;
                        aggregated.SchemaFailed += result.SchemaFailed;
                        aggregated.SchemaIgnored += result.SchemaIgnored;
                        aggregated.Errors.AddRange(result.Errors);
                    }
                    catch (Exception ex)
                    {
                        // 单批失败不中断整个任务：记录错误继续后续批次（进度照常前进）
                        aggregated.Errors.Add($"分组 [{tag}] 导入失败：{ex.Message}");
                        _logger.LogWarning(ex, "Apifox 分批导入失败：项目 {ProjectId}，分组 {Tag}", projectId, tag);
                        _center.Update(handle, p => p.Failed++);
                    }
                    _center.Update(handle, p => p.Completed++);
                }

                // 错误明细截断：批次多时每批一条错误可能很长，保留前 20 条防止响应体膨胀
                aggregated.Errors = aggregated.Errors
                    .Take(20)
                    .Select(e => e.Length <= 300 ? e : e[..300] + "…")
                    .ToList();

                _center.Update(handle, p =>
                {
                    p.Status = "succeeded";
                    p.Current = "";
                    p.Result = aggregated;
                    p.Summary = $"新增 {aggregated.EndpointCreated}、更新 {aggregated.EndpointUpdated}、失败 {aggregated.EndpointFailed}"; // 轮询快照不带 Result，摘要供任务面板/完成通知展示
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Apifox 导入任务失败：项目 {ProjectId}", projectId);
                _center.Fail(handle, ex.Message);
            }
            finally
            {
                _center.Finish(handle);
            }
        }

        /// <summary>把完整 OpenAPI 文档按 operation 的首个 tag 拆成多个子文档；每个子文档只带该批 paths 与按 $ref 闭包裁剪后的 schemas。</summary>
        private static List<(string Tag, string Content)> SplitOpenApiByTag(string content)
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object
                || !paths.EnumerateObject().Any())
                throw new BizException("OpenAPI 内容缺少 paths，请重新解析接口后再导入");

            var schemas = new Dictionary<string, JsonElement>();
            if (root.TryGetProperty("components", out var components)
                && components.ValueKind == JsonValueKind.Object
                && components.TryGetProperty("schemas", out var schemaProp)
                && schemaProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var schema in schemaProp.EnumerateObject())
                    schemas[schema.Name] = schema.Value;
            }

            // tag -> (path, method) 清单，保持文档顺序（tag 即导入 Apifox 后的目录）
            var groups = new List<(string Tag, List<(string Path, string Method)> Items)>();
            var groupIndex = new Dictionary<string, int>();
            foreach (var pathProp in paths.EnumerateObject())
            {
                if (pathProp.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var methodProp in pathProp.Value.EnumerateObject())
                {
                    if (!HttpMethodNames.Contains(methodProp.Name)) continue;
                    var tag = ReadFirstTag(methodProp.Value);
                    if (!groupIndex.TryGetValue(tag, out var gi))
                    {
                        gi = groups.Count;
                        groupIndex[tag] = gi;
                        groups.Add((tag, new List<(string, string)>()));
                    }
                    groups[gi].Items.Add((pathProp.Name, methodProp.Name));
                }
            }

            var batches = new List<(string Tag, string Content)>();
            foreach (var (tag, items) in groups)
            {
                var batchPaths = new JsonObject();
                foreach (var (path, method) in items)
                {
                    var operation = paths.GetProperty(path).GetProperty(method);
                    if (batchPaths[path] is JsonObject pathObj)
                        pathObj[method] = JsonNode.Parse(operation.GetRawText());
                    else
                        batchPaths[path] = new JsonObject { [method] = JsonNode.Parse(operation.GetRawText()) };
                }

                var batch = new JsonObject();
                if (root.TryGetProperty("openapi", out var openapi))
                    batch["openapi"] = openapi.GetString();
                if (root.TryGetProperty("info", out var info))
                    batch["info"] = JsonNode.Parse(info.GetRawText());
                if (root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
                    batch["servers"] = JsonNode.Parse(servers.GetRawText());
                batch["paths"] = batchPaths;

                // components 只带本批 $ref 闭包内的 schema，避免每批都携带全量模型
                var refs = new HashSet<string>();
                CollectSchemaRefs(batchPaths, refs);
                ExpandSchemaClosure(refs, schemas);
                var batchSchemas = new JsonObject();
                foreach (var name in refs.Order(StringComparer.Ordinal))
                    if (schemas.TryGetValue(name, out var schema))
                        batchSchemas[name] = JsonNode.Parse(schema.GetRawText());
                batch["components"] = new JsonObject { ["schemas"] = batchSchemas };

                batches.Add((tag, batch.ToJsonString()));
            }
            return batches;
        }

        private static string ReadFirstTag(JsonElement operation)
        {
            if (operation.ValueKind == JsonValueKind.Object
                && operation.TryGetProperty("tags", out var tags)
                && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tags.EnumerateArray())
                    if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(tag.GetString()))
                        return tag.GetString()!;
            }
            return "未分组";
        }

        /// <summary>递归收集节点内全部 $ref 引用的 schema 名。</summary>
        private static void CollectSchemaRefs(JsonNode? node, HashSet<string> refs)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var prop in obj)
                    {
                        if (prop.Key == "$ref"
                            && prop.Value is JsonValue value
                            && value.TryGetValue<string>(out var refValue)
                            && refValue.StartsWith(SchemaRefPrefix, StringComparison.Ordinal))
                        {
                            refs.Add(refValue[SchemaRefPrefix.Length..]);
                        }
                        else
                        {
                            CollectSchemaRefs(prop.Value, refs);
                        }
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                        CollectSchemaRefs(item, refs);
                    break;
            }
        }

        /// <summary>schema 内部还会 $ref 其他 schema：广度优先把闭包补齐。</summary>
        private static void ExpandSchemaClosure(HashSet<string> refs, Dictionary<string, JsonElement> schemas)
        {
            var queue = new Queue<string>(refs);
            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (!schemas.TryGetValue(name, out var schema)) continue;
                var inner = new HashSet<string>();
                CollectSchemaRefsFromElement(schema, inner);
                foreach (var dependency in inner)
                    if (refs.Add(dependency))
                        queue.Enqueue(dependency);
            }
        }

        private static void CollectSchemaRefsFromElement(JsonElement element, HashSet<string> refs)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (prop.Name == "$ref" && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = prop.Value.GetString() ?? "";
                            if (value.StartsWith(SchemaRefPrefix, StringComparison.Ordinal))
                                refs.Add(value[SchemaRefPrefix.Length..]);
                        }
                        else
                        {
                            CollectSchemaRefsFromElement(prop.Value, refs);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        CollectSchemaRefsFromElement(item, refs);
                    break;
            }
        }

        /// <summary>调用一次 Apifox 导入接口（单批）；HTTP 层失败抛 BizException，由调用方决定是否中断任务。</summary>
        private async Task<ApifoxImportResultDto> ImportSingleAsync(string content, string projectId,
            long? endpointFolderId, long? branchId, string overwriteBehavior, string accessToken)
        {
            var options = new Dictionary<string, object?>
            {
                ["endpointOverwriteBehavior"] = overwriteBehavior,
                ["schemaOverwriteBehavior"] = overwriteBehavior,
                ["updateFolderOfChangedEndpoint"] = false,
                ["prependBasePath"] = false,
                ["deleteUnmatchedResources"] = false,
            };
            if (endpointFolderId is long folderId)
                options["targetEndpointFolderId"] = folderId;
            if (branchId is long branch)
                options["targetBranchId"] = branch;

            var payload = JsonSerializer.Serialize(new { input = content, options }, PayloadJsonOptions);
            var uri = $"https://api.apifox.com/v1/projects/{Uri.EscapeDataString(projectId)}/import-openapi?locale=zh-CN";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            ApplyAuthHeaders(httpRequest, accessToken);

            using var response = await _httpClientFactory.CreateClient("Apifox")
                .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Apifox 导入失败：项目 {ProjectId}，HTTP {StatusCode}", projectId, (int)response.StatusCode);
                throw new BizException($"Apifox 导入失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(body)}");
            }
            return ParseImportResult(body);
        }

        // ==================== 删除任务 ====================

        private async Task RunDeleteAsync(AsyncTaskCenter.AsyncTaskHandle handle, string projectId, long? folderId, string accessToken)
        {
            var result = new ApifoxDeleteResultDto();
            try
            {
                // 阶段一：拉取目录树（删除接口不改变目录结构，一次拉取全程复用）+ 分页拉取接口列表；
                // 指定目录时把子目录 ID 集合一并算出（接口按 folderId 精确归属，需显式包含子目录）
                _center.Update(handle, p =>
                {
                    p.Phase = "listing";
                    p.Current = "正在拉取接口列表...";
                });

                var allFolders = await ListApiFoldersAsync(projectId, accessToken);
                var folderScope = folderId.HasValue ? BuildFolderSubtree(allFolders, folderId.Value) : null;
                var targets = new List<(long Id, string Method, string Path)>();
                for (var page = 1; page <= MaxListPages; page++)
                {
                    var items = await ListHttpApisAsync(projectId, page, ListPerPage, accessToken);
                    foreach (var item in items)
                    {
                        if (folderScope is not null && !folderScope.Contains(item.FolderId ?? 0)) continue;
                        targets.Add((item.Id, item.Method, item.Path));
                    }
                    _center.Update(handle, p => p.Current = $"正在拉取接口列表（已匹配 {targets.Count} 个）...");
                    if (items.Count < ListPerPage) break;
                }

                _center.Update(handle, p =>
                {
                    p.Total = targets.Count;
                    p.Phase = "deleting";
                });

                // 阶段二：受控并发删除（Apifox 无批量删除端点，逐个请求但并行执行，吞吐约提升至并发数倍）；
                // 删除的接口进 Apifox 回收站，30 天内可恢复
                using var semaphore = new SemaphoreSlim(DeleteConcurrency);
                var deletions = targets
                    .Select(target => RunDeleteOneAsync(handle, result, projectId, target, accessToken, semaphore))
                    .ToList();
                await Task.WhenAll(deletions);

                // 阶段三：清理空目录（深度优先删子目录再删父目录；非空或失败忽略，空目录残留不影响使用）。
                // 目录下接口已删空甚至本就没有接口时也要清理，免得残留空目录
                _center.Update(handle, p =>
                {
                    p.Phase = "cleaning";
                    p.Current = "正在清理空目录...";
                });
                {
                    var scope = folderScope ?? allFolders.Select(f => f.Id).ToHashSet();
                    result.FoldersRemoved = await TryRemoveEmptyFoldersAsync(projectId, allFolders, scope, accessToken);
                }

                _center.Update(handle, p =>
                {
                    p.Status = "succeeded";
                    p.Current = "";
                    p.Result = result;
                    p.Summary = result.Failed > 0
                        ? $"删除 {result.Deleted} 个接口，失败 {result.Failed} 个"
                        : $"删除 {result.Deleted} 个接口"; // 轮询快照不带 Result，摘要供任务面板/完成通知展示
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Apifox 批量删除任务失败：项目 {ProjectId}", projectId);
                _center.Fail(handle, ex.Message);
            }
            finally
            {
                _center.Finish(handle);
            }
        }

        /// <summary>删除单个接口（受信号量约束并发执行）：成功/失败原子更新聚合结果与任务进度。</summary>
        private async Task RunDeleteOneAsync(AsyncTaskCenter.AsyncTaskHandle handle, ApifoxDeleteResultDto result, string projectId,
            (long Id, string Method, string Path) target, string accessToken, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                _center.Update(handle, p => p.Current = $"正在删除：{target.Method} {target.Path}");
                await DeleteHttpApiWithRetryAsync(projectId, target.Id, accessToken);
                lock (result)
                {
                    result.Deleted++;
                }
                _center.Update(handle, p => p.Completed++);
            }
            catch (Exception ex)
            {
                lock (result)
                {
                    result.Failed++;
                    if (result.Errors.Count < 20)
                        result.Errors.Add($"{target.Method} {target.Path}：{Truncate(ex.Message)}");
                }
                _center.Update(handle, p =>
                {
                    p.Failed++;
                    p.Completed++;
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>由目录树计算目标目录及其全部子孙目录的 ID 集合。</summary>
        private static HashSet<long> BuildFolderSubtree(List<ApifoxFolderItem> folders, long rootFolderId)
        {
            var childrenMap = new Dictionary<long, List<long>>();
            foreach (var folder in folders)
            {
                var parent = folder.ParentId ?? 0;
                if (!childrenMap.TryGetValue(parent, out var children))
                    childrenMap[parent] = children = new List<long>();
                children.Add(folder.Id);
            }

            var subtree = new HashSet<long> { rootFolderId };
            var queue = new Queue<long>();
            queue.Enqueue(rootFolderId);
            while (queue.Count > 0)
            {
                if (!childrenMap.TryGetValue(queue.Dequeue(), out var children)) continue;
                foreach (var child in children)
                    if (subtree.Add(child))
                        queue.Enqueue(child);
            }
            return subtree;
        }

        /// <summary>删除空目录：按目录深度分层从深到浅（子目录删空后父目录才可能为空），同层目录受控并发，失败忽略。</summary>
        private async Task<int> TryRemoveEmptyFoldersAsync(string projectId, List<ApifoxFolderItem> allFolders,
            HashSet<long> scope, string accessToken)
        {
            var parentMap = allFolders.ToDictionary(f => f.Id, f => f.ParentId);
            var layers = allFolders
                .Where(f => scope.Contains(f.Id))
                .GroupBy(f => FolderDepth(f, parentMap))
                .OrderByDescending(g => g.Key);
            var removed = 0;
            using var semaphore = new SemaphoreSlim(DeleteConcurrency);
            foreach (var layer in layers)
            {
                var deletions = layer.Select(async folder =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await DeleteApiFolderAsync(projectId, folder.Id, accessToken);
                        return true;
                    }
                    catch
                    {
                        // 目录非空或不允许删除：忽略（空目录留在项目里不影响使用）
                        return false;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();
                var results = await Task.WhenAll(deletions);
                removed += results.Count(ok => ok);
            }
            return removed;
        }

        private static int FolderDepth(ApifoxFolderItem folder, Dictionary<long, long?> parentMap)
        {
            var depth = 0;
            var current = folder.ParentId;
            while (current.HasValue && depth < 100)
            {
                depth++;
                current = parentMap.TryGetValue(current.Value, out var parent) ? parent : null;
            }
            return depth;
        }

        /// <summary>分页拉取项目接口列表（id/方法/路径/所属目录）。</summary>
        private async Task<List<ApifoxEndpointItem>> ListHttpApisAsync(string projectId, int page, int perPage,
            string accessToken)
        {
            var uri = $"https://api.apifox.com/api/v1/projects/{Uri.EscapeDataString(projectId)}"
                      + $"/http-apis?page={page}&perPage={perPage}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuthHeaders(httpRequest, accessToken);
            using var response = await _httpClientFactory.CreateClient("Apifox").SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new BizException($"拉取 Apifox 接口列表失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(body)}");

            var items = new List<ApifoxEndpointItem>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var id) || id <= 0)
                        continue;
                    var method = item.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";
                    var path = item.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                    long? folderId = item.TryGetProperty("folderId", out var folderProp)
                                     && folderProp.ValueKind == JsonValueKind.Number
                                     && folderProp.TryGetInt64(out var folderValue)
                        ? folderValue
                        : null;
                    items.Add(new ApifoxEndpointItem(id, method.ToUpperInvariant(), path, folderId));
                }
            }
            return items;
        }

        private async Task<List<ApifoxFolderItem>> ListApiFoldersAsync(string projectId, string accessToken)
        {
            var uri = $"https://api.apifox.com/api/v1/projects/{Uri.EscapeDataString(projectId)}/api-folders";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuthHeaders(httpRequest, accessToken);
            using var response = await _httpClientFactory.CreateClient("Apifox").SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new BizException($"拉取 Apifox 目录树失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(body)}");

            var folders = new List<ApifoxFolderItem>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var id) || id <= 0)
                        continue;
                    long? parentId = item.TryGetProperty("parentId", out var parentProp)
                                     && parentProp.ValueKind == JsonValueKind.Number
                                     && parentProp.TryGetInt64(out var parentValue)
                        ? parentValue
                        : null;
                    folders.Add(new ApifoxFolderItem(id, parentId));
                }
            }
            return folders;
        }

        /// <summary>单接口删除一次调用；不抛异常，返回状态码与错误消息由调用方决定重试或计失败。</summary>
        private async Task<(bool Success, int StatusCode, string Error)> DeleteHttpApiOnceAsync(
            string projectId, long endpointId, string accessToken)
        {
            var uri = $"https://api.apifox.com/api/v1/projects/{Uri.EscapeDataString(projectId)}/http-apis/{endpointId}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, uri);
            ApplyAuthHeaders(httpRequest, accessToken);
            using var response = await _httpClientFactory.CreateClient("Apifox").SendAsync(httpRequest);
            if (response.IsSuccessStatusCode) return (true, (int)response.StatusCode, "");
            var body = await response.Content.ReadAsStringAsync();
            return (false, (int)response.StatusCode, ExtractErrorMessage(body));
        }

        /// <summary>单接口删除（带瞬态重试）：429/502/503/504 与网络异常按次数线性退避重试，仍失败抛 BizException。</summary>
        private async Task DeleteHttpApiWithRetryAsync(string projectId, long endpointId, string accessToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var (success, statusCode, error) = await DeleteHttpApiOnceAsync(projectId, endpointId, accessToken);
                    if (success) return;
                    if (!RetryableStatusCodes.Contains(statusCode) || attempt >= DeleteMaxRetries)
                        throw new BizException($"HTTP {statusCode}：{error}");
                }
                catch (HttpRequestException) when (attempt < DeleteMaxRetries)
                {
                    // 网络瞬态异常（连接重置/超时）同样退避重试
                }
                await Task.Delay(DeleteRetryDelayMs * (attempt + 1));
            }
        }

        private async Task DeleteApiFolderAsync(string projectId, long folderId, string accessToken)
        {
            var uri = $"https://api.apifox.com/api/v1/projects/{Uri.EscapeDataString(projectId)}/api-folders/{folderId}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, uri);
            ApplyAuthHeaders(httpRequest, accessToken);
            using var response = await _httpClientFactory.CreateClient("Apifox").SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new BizException(ExtractErrorMessage(body));
            }
        }

        private sealed record ApifoxEndpointItem(long Id, string Method, string Path, long? FolderId);

        private sealed record ApifoxFolderItem(long Id, long? ParentId);

        // ==================== 校验与解析 ====================

        private string ResolveAccessToken()
        {
            var accessToken = UnprotectAccessToken(_userConfigService.GetRawValue(TokenKey));
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new BizException("请先到个人配置保存 Apifox Access Token");
            return accessToken;
        }

        private string? UnprotectAccessToken(string? protectedToken)
        {
            if (string.IsNullOrWhiteSpace(protectedToken)) return null;
            try
            {
                return _tokenProtector.Unprotect(protectedToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法解密当前用户保存的 Apifox Access Token");
                throw new BizException("已保存的 Apifox Access Token 无法读取，请重新保存配置");
            }
        }

        private static void ApplyAuthHeaders(HttpRequestMessage request, string accessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("X-Apifox-Api-Version", ApiVersion);
        }

        private static void ValidateOpenApiContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new BizException("OpenAPI 内容不能为空，请重新解析接口后再导入");
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("openapi", out _))
                    throw new BizException("仅支持导入 OpenAPI 3 JSON 内容");
            }
            catch (JsonException)
            {
                throw new BizException("生成的 OpenAPI JSON 无法解析，请重新解析接口后再导入");
            }
        }

        private static string ValidateProjectId(string? value)
        {
            var projectId = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(projectId)) throw new BizException("请填写 Apifox 项目 ID");
            if (projectId.Length > 64 || projectId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new BizException("Apifox 项目 ID 格式不合法");
            return projectId;
        }

        private static long? ValidateOptionalId(long? value, string name)
        {
            if (value == null) return null;
            if (value <= 0) throw new BizException($"{name}必须是正整数");
            return value;
        }

        private static string NormalizeOverwriteBehavior(string? value)
        {
            var behavior = (value ?? "AUTO_MERGE").Trim().ToUpperInvariant();
            return OverwriteBehaviors.Contains(behavior) ? behavior : "AUTO_MERGE";
        }

        private static ApifoxImportResultDto ParseImportResult(string body)
        {
            var result = new ApifoxImportResultDto();
            if (string.IsNullOrWhiteSpace(body)) return result;

            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("data", out var data)) return result;
                if (data.TryGetProperty("counters", out var counters))
                {
                    result.EndpointCreated = ReadCounter(counters, "endpointCreated");
                    result.EndpointUpdated = ReadCounter(counters, "endpointUpdated");
                    result.EndpointFailed = ReadCounter(counters, "endpointFailed");
                    result.EndpointIgnored = ReadCounter(counters, "endpointIgnored");
                    result.SchemaCreated = ReadCounter(counters, "schemaCreated");
                    result.SchemaUpdated = ReadCounter(counters, "schemaUpdated");
                    result.SchemaFailed = ReadCounter(counters, "schemaFailed");
                    result.SchemaIgnored = ReadCounter(counters, "schemaIgnored");
                }
                if (data.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    result.Errors = errors.EnumerateArray()
                        .Select(error => error.TryGetProperty("message", out var message) ? message.GetString() : null)
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Select(message => message!)
                        .Distinct()
                        .ToList();
                }
            }
            catch (JsonException)
            {
                // Apifox 返回成功但内容非 JSON 时不阻断导入；前端仍可给出已提交提示。
            }
            return result;
        }

        private static int ReadCounter(JsonElement counters, string property)
            => counters.TryGetProperty(property, out var value) && value.TryGetInt32(out var count) ? count : 0;

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "未返回详细信息";
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                foreach (var key in new[] { "message", "msg", "error", "errorMessage" })
                {
                    if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                        return Truncate(value.GetString() ?? "未返回详细信息");
                }
            }
            catch (JsonException)
            {
                // 退回原始响应文本。
            }
            return Truncate(body);
        }

        private static string Truncate(string value)
            => value.Length <= 500 ? value : value[..500] + "…";
    }
}
