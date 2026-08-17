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
    /// 企业微信应用消息（私聊或小群发送）。
    /// 流程：先用 CorpId + CorpSecret 获取 Token（2小时有效），再用 Token 发送消息给指定用户/部门。
    /// Token 缓存在内存（ConcurrentDictionary），避免频繁申请。
    /// 文档：https://developer.work.weixin.qq.com/document/path/90236
    /// </summary>
    public class WeComPrivateProvider : IWebhookProvider
    {
        private readonly ILogger<WeComPrivateProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>Token 缓存：{corpId} -> {token, expireTime}</summary>
        private static readonly ConcurrentDictionary<string, (string token, DateTime expireTime)> _tokenCache = new();

        public string ProviderType => "wecom-private";

        public WeComPrivateProvider(ILogger<WeComPrivateProvider> logger)
        {
            _logger = logger;
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 解析配置（AppKey=CorpId, AppSecret=CorpSecret, WebhookUrl 中存 AgentId）
                var agentId = ExtractAgentId(cfg.WebhookUrl);
                if (string.IsNullOrEmpty(agentId))
                    return WebhookSendResult.Fail("未配置 AgentId（从 Webhook 字段提取）", 0);

                var recipientIds = ParseRecipientIds(cfg.RecipientIds);
                if (recipientIds.Count == 0)
                    return WebhookSendResult.Fail("未配置接收者列表", 0);

                // 获取 Token（有缓存）
                var token = await GetAccessTokenAsync(cfg.AppKey, cfg.AppSecret);
                if (string.IsNullOrEmpty(token))
                    return WebhookSendResult.Fail("获取 Token 失败", (int)sw.ElapsedMilliseconds);

                // 构造消息内容
                var text = string.IsNullOrEmpty(title) ? content : $"{title}\n{content}";

                // 并发发送给所有接收者
                var tasks = recipientIds.Select(uid => SendToUserAsync(token, agentId, uid, text)).ToList();
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
                _logger.LogError(ex, "企业微信应用推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }

        /// <summary>获取 Token（带缓存机制）。</summary>
        private async Task<string?> GetAccessTokenAsync(string? corpId, string? corpSecret)
        {
            if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(corpSecret))
                return null;

            // 检查缓存
            if (_tokenCache.TryGetValue(corpId, out var cached) && DateTime.UtcNow < cached.expireTime.AddSeconds(-60))
                return cached.token;

            try
            {
                // GET 获取 Token
                var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(corpId)}&corpsecret={Uri.EscapeDataString(corpSecret)}";
                var resp = await _http.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(body);

                // 从响应提取 access_token 和 expires_in
                if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl) ||
                    !doc.RootElement.TryGetProperty("expires_in", out var expireEl))
                {
                    _logger.LogError("获取企业微信 Token 失败: {Response}", body);
                    return null;
                }

                var token = tokenEl.GetString();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogError("获取企业微信 Token 失败：token 为空");
                    return null;
                }

                var expireSeconds = expireEl.TryGetInt32(out var sec) ? sec : 7200; // 默认 2 小时

                // 更新缓存
                var expireTime = DateTime.UtcNow.AddSeconds(expireSeconds);
                _tokenCache[corpId] = (token, expireTime);

                _logger.LogInformation("获取企业微信 Token 成功，有效期 {Seconds} 秒", expireSeconds);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "请求企业微信 Token 异常");
                return null;
            }
        }

        /// <summary>发送消息给单个用户。</summary>
        private async Task<(bool success, string uid, string error)> SendToUserAsync(string token, string agentId, string userId, string content)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    touser = userId,
                    msgtype = "text",
                    agentid = int.Parse(agentId),
                    text = new { content }
                });

                var resp = await _http.PostAsync(
                    $"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={Uri.EscapeDataString(token)}",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

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

        /// <summary>从 WebhookUrl 中提取 AgentId（格式：agentid:123）。</summary>
        private static string? ExtractAgentId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var parts = url.Split(':');
            return parts.Length > 1 ? parts[^1].Trim() : null;
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
