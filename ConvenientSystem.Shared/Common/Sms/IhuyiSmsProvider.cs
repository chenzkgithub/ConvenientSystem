using System.Diagnostics;
using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 互亿无线短信 Provider（HTTP API，无需 SDK）
    /// 文档：https://www.ihuyi.com/api/sms.html
    /// </summary>
    public class IhuyiSmsProvider : ISmsProvider
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<IhuyiSmsProvider> _logger;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public string Name => "ihuyi";

        public IhuyiSmsProvider(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<IhuyiSmsProvider> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public async Task<SmsSendResult> SendAsync(string phone, string content, string signature)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var config = _fsql.Select<SmsProviderConfigEntity>()
                    .OrderByDescending(c => c.Id)
                    .First();
                if (config == null)
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ErrorMessage = "未配置短信密钥，请先在【系统配置】页面填写",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                var apiId = config.AccessKeyId;
                var apiKey = config.AccessKeySecret;

                if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(apiKey))
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ErrorMessage = "未配置互亿无线密钥，请先到【系统配置】页面填写 API ID 和 API Key",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                // 互亿无线内容格式：【签名】正文
                var fullContent = $"【{signature}】{content}";

                var form = new Dictionary<string, string>
                {
                    ["account"] = apiId,
                    ["password"] = apiKey,
                    ["mobile"] = phone,
                    ["content"] = fullContent,
                    ["format"] = "json"
                };

                var resp = await _http.PostAsync(
                    "https://106.ihuyi.com/webservice/sms.php?method=Submit",
                    new FormUrlEncodedContent(form));
                sw.Stop();

                var body = await resp.Content.ReadAsStringAsync();
                var json = System.Text.Json.JsonDocument.Parse(body);
                var root = json.RootElement;

                var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : -1;
                var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "未知错误";
                var smsId = root.TryGetProperty("smsid", out var sidEl) ? sidEl.GetString() : null;

                var ok = code == 0;
                return new SmsSendResult
                {
                    Success = ok,
                    ProviderMsgId = smsId,
                    ErrorMessage = ok ? null : $"code={code}: {msg}",
                    CostMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "互亿无线短信发送异常：phone={Phone}", phone);
                return new SmsSendResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CostMs = (int)sw.ElapsedMilliseconds
                };
            }
        }
    }
}
