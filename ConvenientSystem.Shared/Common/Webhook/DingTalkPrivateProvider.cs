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
    /// 钉钉个人机器人（私聊模式）。
    /// 流程：先用 AppKey + AppSecret 获取 Token（2小时有效），再用 Token 发送消息给指定用户。
    /// Token 缓存在内存（ConcurrentDictionary），避免频繁申请。
    /// 文档：https://open.dingtalk.com/document/robots/private-robot-access
    /// </summary>
    public class DingTalkPrivateProvider : IWebhookProvider
    {
        private readonly ILogger<DingTalkPrivateProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>Token 缓存：{appKey} -> {token, expireTime}</summary>
        private static readonly ConcurrentDictionary<string, (string token, DateTime expireTime)> _tokenCache = new();

        public string ProviderType => "dingtalk-private";

        public DingTalkPrivateProvider(ILogger<DingTalkPrivateProvider> logger)
        {
            _logger = logger;
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 解析接收者
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
                var tasks = recipientIds.Select(uid => SendToUserAsync(token, uid, text, cfg.AppKey)).ToList();
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
                _logger.LogError(ex, "钉钉私聊机器人推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }

        /// <summary>获取 Token（带缓存机制）。</summary>
        private async Task<string?> GetAccessTokenAsync(string? appKey, string? appSecret)
        {
            if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(appSecret))
                return null;

            // 检查缓存
            if (_tokenCache.TryGetValue(appKey, out var cached) && DateTime.UtcNow < cached.expireTime.AddSeconds(-60))
                return cached.token;

            try
            {
                // POST 获取 Token
                var payload = JsonSerializer.Serialize(new { appKey, appSecret });
                var resp = await _http.PostAsync(
                    "https://api.dingtalk.com/v1.0/oauth2/accessToken",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                // 从响应提取 accessToken 和 expireTime
                if (!doc.RootElement.TryGetProperty("accessToken", out var tokenEl) ||
                    !doc.RootElement.TryGetProperty("expireTime", out var expireEl))
                {
                    _logger.LogError("获取钉钉 Token 失败: {Response}", body);
                    return null;
                }

                var token = tokenEl.GetString();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("获取钉钉 Token 失败：token 为空");
                    return null;
                }
                var expireSeconds = expireEl.TryGetInt32(out var sec) ? sec : 7200; // 默认 2 小时

                // 更新缓存
                var expireTime = DateTime.UtcNow.AddSeconds(expireSeconds);
                _tokenCache[appKey] = (token, expireTime);

                _logger.LogInformation("获取钉钉 Token 成功，有效期 {Seconds} 秒", expireSeconds);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "请求钉钉 Token 异常");
                return null;
            }
        }

        /// <summary>发送消息给单个用户。</summary>
        private async Task<(bool success, string uid, string error)> SendToUserAsync(string token, string userId, string content, string? appKey)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    robotCode = appKey, // 钉钉官方：robotCode 即企业内部机器人应用的 AppKey
                    userIds = new[] { userId },
                    msgKey = "sampleText",
                    msgParam = JsonSerializer.Serialize(new { content })
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dingtalk.com/v1.0/robot/oToMessages/batchSend")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                    Headers = { { "Authorization", $"Bearer {token}" } }
                };

                var resp = await _http.SendAsync(request);
                var body = await resp.Content.ReadAsStringAsync();

                // 检查 errcode
                var errcode = TryGetInt(body, "errcode");
                if (errcode == 0)
                    return (true, userId, "");

                return (false, userId, $"errcode={errcode}: {body}");
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
