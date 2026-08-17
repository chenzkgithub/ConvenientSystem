using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConvenientSystem.Shared.Entity.Common;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 飞书群自定义机器人。UseCard=false 发 text，UseCard=true 发 post 富文本（加签时都包 timestamp/sign）。
    /// sign = base64(HmacSHA256(key = timestamp + "\n" + secret, data = 空))，timestamp 为秒。
    /// 文档：https://open.feishu.cn/document/client-docs/bot-v3/add-custom-bot
    /// </summary>
    public class FeishuProvider : IWebhookProvider
    {
        private readonly ILogger<FeishuProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public string ProviderType => "feishu";

        public FeishuProvider(ILogger<FeishuProvider> logger)
        {
            _logger = logger;
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 飞书群机器人：纯文本用 text，富文本卡片用 post（加签时两者都包 timestamp/sign）
                string msgType;
                object contentObj;
                if (cfg.UseCard)
                {
                    // post 富文本：标题作卡片标题，内容按 \n 拆成多段文本节点
                    msgType = "post";
                    var postText = string.IsNullOrEmpty(title) ? content : $"【{title}】\n{content}";
                    var nodes = postText.Split('\n').Select(l => (object)new { tag = "text", text = l }).ToArray();
                    contentObj = new
                    {
                        post = new
                        {
                            zh_cn = new
                            {
                                title = title ?? "消息",
                                content = new[] { nodes }
                            }
                        }
                    };
                }
                else
                {
                    msgType = "text";
                    var text = string.IsNullOrEmpty(title) ? content : $"【{title}】\n{content}";
                    contentObj = new { text };
                }

                object payload;
                if (!string.IsNullOrEmpty(cfg.Secret))
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var stringToSign = $"{timestamp}\n{cfg.Secret}";
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
                    var sign = Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
                    payload = new
                    {
                        timestamp = timestamp.ToString(),
                        sign,
                        msg_type = msgType,
                        content = contentObj
                    };
                }
                else
                {
                    payload = new { msg_type = msgType, content = contentObj };
                }

                var json = JsonSerializer.Serialize(payload);
                var resp = await _http.PostAsync(cfg.WebhookUrl,
                    new StringContent(json, Encoding.UTF8, "application/json"));
                sw.Stop();

                var body = await resp.Content.ReadAsStringAsync();
                // 飞书成功：{"code":0,...} 或旧版 {"StatusCode":0,...}
                var code = DingTalkProvider.TryGetInt(body, "code") ?? DingTalkProvider.TryGetInt(body, "StatusCode");
                if (code == 0)
                    return WebhookSendResult.Ok((int)sw.ElapsedMilliseconds);
                return WebhookSendResult.Fail($"飞书返回：{body}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "飞书机器人推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }
    }
}
