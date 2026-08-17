using System.Diagnostics;
using Aliyun.Acs;
using Aliyun.Acs.Core;
using Aliyun.Acs.Core.Http;
using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 阿里云短信 Provider（基于 aliyun-net-sdk-core）
    /// 调用阿里云 SDK 发送短信；密钥从数据库读取并解密。
    /// </summary>
    public class AliyunSmsProvider : ISmsProvider
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<AliyunSmsProvider> _logger;

        public string Name => "aliyun";

        public AliyunSmsProvider(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<AliyunSmsProvider> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public async Task<SmsSendResult> SendAsync(string phone, string content, string signature)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 从数据库读取并解密配置
                var config = _fsql.Select<SmsProviderConfigEntity>()
                    .OrderByDescending(c => c.Id)
                    .First();
                if (config == null)
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ErrorMessage = "未配置阿里云短信密钥，请先在【系统配置】页面填写",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                var accessKeyId = config.AccessKeyId;
                var accessKeySecret = config.AccessKeySecret;

                if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(accessKeySecret))
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ErrorMessage = "未配置阿里云密钥，请先到【系统配置】页面填写 AccessKey",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                // 构造阿里云 API 客户端
                var profile = Aliyun.Acs.Core.Profile.DefaultProfile.GetProfile(
                    "cn-hangzhou", accessKeyId, accessKeySecret);
                IAcsClient client = new DefaultAcsClient(profile);

                // 构造 SendSms 请求
                var request = new CommonRequest
                {
                    Method = MethodType.POST,
                    Domain = "dysmsapi.aliyuncs.com",
                    Version = "2017-05-25",
                    Action = "SendSms"
                };
                request.AddQueryParameters("PhoneNumbers", phone);
                request.AddQueryParameters("SignName", signature);
                request.AddQueryParameters("TemplateCode", string.IsNullOrEmpty(config.TemplateCode) ? "SMS_TEMPLATE_CODE" : config.TemplateCode);
                request.AddQueryParameters("TemplateParam",
                    System.Text.Json.JsonSerializer.Serialize(new { content }));

                // 异步执行（包装同步 SDK 调用）
                var response = await Task.Run(() => client.GetCommonResponse(request));
                sw.Stop();

                var body = System.Text.Encoding.UTF8.GetString(response.HttpResponse.Content);
                var json = System.Text.Json.JsonDocument.Parse(body);
                var code = json.RootElement.TryGetProperty("Code", out var codeEl) ? codeEl.GetString() : null;
                var ok = string.Equals(code, "OK", StringComparison.OrdinalIgnoreCase);
                var requestId = json.RootElement.TryGetProperty("RequestId", out var ridEl) ? ridEl.GetString() : null;
                var message = json.RootElement.TryGetProperty("Message", out var msgEl) ? msgEl.GetString() : null;

                return new SmsSendResult
                {
                    Success = ok,
                    ProviderMsgId = requestId,
                    ErrorMessage = ok ? null : $"{code}: {message}",
                    CostMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "阿里云短信发送异常：phone={Phone}", phone);
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
