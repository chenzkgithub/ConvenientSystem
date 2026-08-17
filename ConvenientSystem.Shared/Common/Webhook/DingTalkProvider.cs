using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConvenientSystem.Shared.Entity.Common;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 钉钉群自定义机器人。UseCard=false 发 text，UseCard=true 发 markdown（富文本卡片）。
    /// 若配置了 Secret 则按官方规则加签：sign = base64(HmacSHA256(timestamp + "\n" + secret, secret))，追加到 URL。
    /// 文档：https://open.dingtalk.com/document/robots/custom-robot-access
    /// </summary>
    public class DingTalkProvider : IWebhookProvider
    {
        private readonly ILogger<DingTalkProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public string ProviderType => "dingtalk";

        public DingTalkProvider(ILogger<DingTalkProvider> logger)
        {
            _logger = logger;
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var url = cfg.WebhookUrl;
                if (!string.IsNullOrEmpty(cfg.Secret))
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var stringToSign = $"{timestamp}\n{cfg.Secret}";
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(cfg.Secret));
                    var sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    var sep = url.Contains('?') ? "&" : "?";
                    url = $"{url}{sep}timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
                }

                object payloadObj;
                if (cfg.UseCard)
                {
                    // 富文本卡片：钉钉群机器人 markdown，标题作首行标题，内容支持 Markdown 语法
                    var md = string.IsNullOrEmpty(title) ? content : $"### {title}\n\n{content}";
                    payloadObj = new
                    {
                        msgtype = "markdown",
                        markdown = new { title = title ?? "消息", text = md }
                    };
                }
                else
                {
                    var text = string.IsNullOrEmpty(title) ? content : $"【{title}】\n{content}";
                    payloadObj = new
                    {
                        msgtype = "text",
                        text = new { content = text }
                    };
                }
                var payload = JsonSerializer.Serialize(payloadObj);

                var resp = await _http.PostAsync(url,
                    new StringContent(payload, Encoding.UTF8, "application/json"));
                sw.Stop();

                var body = await resp.Content.ReadAsStringAsync();
                var errcode = TryGetInt(body, "errcode");
                if (errcode == 0)
                    return WebhookSendResult.Ok((int)sw.ElapsedMilliseconds);
                return WebhookSendResult.Fail($"钉钉返回：{body}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "钉钉机器人推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }

        internal static int? TryGetInt(string json, string prop)
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
