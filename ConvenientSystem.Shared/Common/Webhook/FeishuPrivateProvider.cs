using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConvenientSystem.Shared.Entity.Common;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 飞书应用消息（私聊模式）。
    /// 流程：先用 AppId + AppSecret 获取 Token（2小时有效），再用 Token 发送消息给指定用户。
    /// Token 缓存在内存（ConcurrentDictionary），避免频繁申请。
    /// 文档：https://open.feishu.cn/document/server-docs/im-v1/message/create
    /// </summary>
    public class FeishuPrivateProvider : IWebhookProvider
    {
        private readonly ILogger<FeishuPrivateProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>Token 缓存：{appId} -> {token, expireTime}</summary>
        private static readonly ConcurrentDictionary<string, (string token, DateTime expireTime)> _tokenCache = new();

        public string ProviderType => "feishu-private";

        public FeishuPrivateProvider(ILogger<FeishuPrivateProvider> logger)
        {
            _logger = logger;
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var recipientIds = ParseRecipientIds(cfg.RecipientIds);
                if (recipientIds.Count == 0)
                    return WebhookSendResult.Fail("未配置接收者列表", 0);

                // 获取 Token（有缓存）
                var token = await GetAccessTokenAsync(cfg.AppKey, cfg.AppSecret);
                if (string.IsNullOrEmpty(token))
                    return WebhookSendResult.Fail("获取 Token 失败", (int)sw.ElapsedMilliseconds);

                // 构造消息内容
                var text = string.IsNullOrEmpty(title) ? content : $"【{title}】\n{content}";

                // 并发发送给所有接收者
                var tasks = recipientIds.Select(uid => SendToUserAsync(token, uid, text)).ToList();
                var results = await Task.WhenAll(tasks);

                sw.Stop();

                // 检查发送结果
                var failed = results.Where(r => !r.success).ToList();
                if (failed.Count == 0)
                    return WebhookSendResult.Ok((int)sw.ElapsedMilliseconds);

                var errors = string.Join("; ", failed.Select(r => $"{r.uid}:{r.error}"));
                return WebhookSendResult.Fail($"部分接收者失败: {errors}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "飞书应用推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }

        /// <summary>获取 Token（带缓存机制）。</summary>
        private async Task<string?> GetAccessTokenAsync(string? appId, string? appSecret)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
                return null;

            // 检查缓存
            if (_tokenCache.TryGetValue(appId, out var cached) && DateTime.UtcNow < cached.expireTime.AddSeconds(-60))
                return cached.token;

            try
            {
                // POST 获取 Token
                var payload = JsonSerializer.Serialize(new { app_id = appId, app_secret = appSecret });
                var resp = await _http.PostAsync(
                    "https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                // 从响应提取 app_access_token 和 expire
                if (!doc.RootElement.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
                {
                    _logger.LogError("获取飞书 Token 失败: {Response}", body);
                    return null;
                }

                if (!doc.RootElement.TryGetProperty("app_access_token", out var tokenEl))
                {
                    _logger.LogError("获取飞书 Token 失败：token 字段缺失");
                    return null;
                }

                var token = tokenEl.GetString();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("获取飞书 Token 失败：token 为空");
                    return null;
                }

                var expireSeconds = doc.RootElement.TryGetProperty("expire", out var expireEl) &&
                                    expireEl.TryGetInt32(out var sec) ? sec : 7200; // 默认 2 小时

                // 更新缓存
                var expireTime = DateTime.UtcNow.AddSeconds(expireSeconds);
                _tokenCache[appId] = (token, expireTime);

                _logger.LogInformation("获取飞书 Token 成功，有效期 {Seconds} 秒", expireSeconds);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "请求飞书 Token 异常");
                return null;
            }
        }

        /// <summary>发送消息给单个用户。</summary>
        private async Task<(bool success, string uid, string error)> SendToUserAsync(string token, string userId, string content)
        {
            try
            {
                // 飞书采用 POST 方式发送消息，user_id 作为路径参数
                var payload = JsonSerializer.Serialize(new
                {
                    receive_id_type = "user_id",
                    msg_type = "text",
                    content = JsonSerializer.Serialize(new { text = content })
                });

                var request = new HttpRequestMessage(HttpMethod.Post, 
                    $"https://open.feishu.cn/open-apis/im/v1/messages?receive_id={Uri.EscapeDataString(userId)}")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                    Headers = { { "Authorization", $"Bearer {token}" } }
                };

                var resp = await _http.SendAsync(request);
                var body = await resp.Content.ReadAsStringAsync();

                // 检查 code（0 表示成功）
                using var doc = JsonDocument.Parse(body);
                var code = TryGetInt(body, "code");
                if (code == 0)
                    return (true, userId, "");

                return (false, userId, $"code={code}: {body}");
            }
            catch (Exception ex)
            {
                return (false, userId, ex.Message);
            }
        }

        /// <summary>解析接收者列表（支持 JSON 或逗号分隔）。</summary>
        private List<string> ParseRecipientIds(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return new();

            try
            {
                // 尝试按 JSON 数组解析
                if (ids.TrimStart().StartsWith("["))
                {
                    using var doc = JsonDocument.Parse(ids);
                    return doc.RootElement.EnumerateArray()
                        .Select(el => el.GetString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList()!;
                }
            }
            catch { }

            // 降级为逗号分隔
            return ids.Split(',', ';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        /// <summary>从 JSON 字符串提取 int 属性。</summary>
        private static int? TryGetInt(string json, string prop)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(prop, out var el) && el.TryGetInt32(out var v))
                    return v;
            }
            catch { }
            return null;
        }
    }
}
