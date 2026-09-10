using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.DataProtection;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Apifox OpenAPI 导入实现。
    /// 导入配置保存到当前用户的 UserConfig；Access Token 通过 ASP.NET Core Data Protection 加密后才落库。
    /// </summary>
    public sealed class ApifoxImportService : IApifoxImportService
    {
        private const string TokenKey = "ApiSpec.Apifox.AccessToken";
        private const string ApiVersion = "2024-03-28";

        private static readonly HashSet<string> OverwriteBehaviors = new(StringComparer.Ordinal)
        {
            "OVERWRITE_EXISTING", "AUTO_MERGE", "KEEP_EXISTING", "CREATE_NEW",
        };

        private static readonly JsonSerializerOptions PayloadJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IUserConfigService _userConfigService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _tokenProtector;
        private readonly ILogger<ApifoxImportService> _logger;

        public ApifoxImportService(
            IUserConfigService userConfigService,
            IHttpClientFactory httpClientFactory,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<ApifoxImportService> logger)
        {
            _userConfigService = userConfigService;
            _httpClientFactory = httpClientFactory;
            _tokenProtector = dataProtectionProvider.CreateProtector("ConvenientSystem.ApiSpec.Apifox.AccessToken.v1");
            _logger = logger;
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

        public async Task<ApifoxImportResultDto> ImportAsync(ApifoxImportRequest request, CancellationToken cancellationToken = default)
        {
            ValidateOpenApiContent(request.Content);
            var projectId = ValidateProjectId(request.ProjectId);
            var overwriteBehavior = NormalizeOverwriteBehavior(request.EndpointOverwriteBehavior);
            var endpointFolderId = ValidateOptionalId(request.TargetEndpointFolderId, "接口目录 ID");
            var branchId = ValidateOptionalId(request.TargetBranchId, "分支 ID");

            var accessToken = UnprotectAccessToken(_userConfigService.GetRawValue(TokenKey));
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new BizException("请先到个人配置保存 Apifox Access Token");

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

            var payload = JsonSerializer.Serialize(new { input = request.Content, options }, PayloadJsonOptions);
            var uri = $"https://api.apifox.com/v1/projects/{Uri.EscapeDataString(projectId)}/import-openapi?locale=zh-CN";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Headers.Add("X-Apifox-Api-Version", ApiVersion);

            HttpResponseMessage response;
            try
            {
                response = await _httpClientFactory.CreateClient("Apifox")
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "调用 Apifox 导入接口失败，项目 {ProjectId}", projectId);
                throw new BizException("无法连接 Apifox 开放 API，请检查网络后重试");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Apifox 导入失败：项目 {ProjectId}，HTTP {StatusCode}", projectId, (int)response.StatusCode);
                    throw new BizException($"Apifox 导入失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(body)}");
                }

                return ParseImportResult(body);
            }
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
                foreach (var key in new[] { "message", "msg", "error" })
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
