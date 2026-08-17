using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 代码命名翻译服务：优先调用百度翻译 API（精准中英翻译），
    /// 失败时回退至 MyMemory 免费 API，最终由前端拼音兜底。
    /// 百度 API 文档：https://fanyi-api.baidu.com/doc/21
    /// </summary>
    public class CodeNamingService : ICodeNamingService
    {
        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        private readonly ISysConfigService _sysConfig;

        public CodeNamingService(ISysConfigService sysConfig)
        {
            _sysConfig = sysConfig;
        }

        public CodeNamingTranslateDto Translate(string text)
        {
            var input = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            if (input.Length == 0)
                return new CodeNamingTranslateDto { Original = input, Translated = "", Words = new() };

            // 优先：百度翻译 API（精准中英翻译）— 每次从 SysConfig 动态读取，修改后即时生效
            var appId = _sysConfig.GetValue("BaiduTranslate.AppId");
            var secret = _sysConfig.GetValue("BaiduTranslate.Secret");
            if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(secret))
            {
                var baiduResult = TranslateViaBaidu(input, appId, secret);
                if (baiduResult != null)
                {
                    var words = SplitWords(baiduResult);
                    if (words.Count > 0)
                        return new CodeNamingTranslateDto
                        {
                            Original = input,
                            Translated = string.Join(" ", words),
                            Words = words,
                        };
                }
            }

            // 回退：MyMemory 免费 API
            var mmResult = TranslateViaMyMemory(input);
            if (mmResult != null)
            {
                var words = SplitWords(mmResult);
                if (words.Count > 0)
                    return new CodeNamingTranslateDto
                    {
                        Original = input,
                        Translated = string.Join(" ", words),
                        Words = words,
                    };
            }

            // 最终：返回空，前端拼音兜底
            return new CodeNamingTranslateDto
            {
                Original = input,
                Translated = "",
                Words = new(),
            };
        }

        /// <summary>百度翻译 API（MD5 签名认证，免费额度 6 万字符/月）</summary>
        private string? TranslateViaBaidu(string text, string appId, string secret)
        {
            try
            {
                var salt = Guid.NewGuid().ToString("N");
                // 签名 = MD5(appid + 原文 + salt + secret)，注意用原始文本而非 URL 编码后的
                var sign = ComputeMd5($"{appId}{text}{salt}{secret}");
                var url = "https://fanyi-api.baidu.com/api/trans/vip/translate"
                          + $"?q={Uri.EscapeDataString(text)}"
                          + "&from=zh&to=en"
                          + $"&appid={appId}"
                          + $"&salt={salt}"
                          + $"&sign={sign}";

                var resp = _http.GetFromJsonAsync<BaiduResponse>(url).GetAwaiter().GetResult();

                // 百度错误响应：{"error_code":"54001","error_msg":"Invalid Sign"}
                if (!string.IsNullOrEmpty(resp?.Error_Code))
                    return null;

                if (resp?.Trans_Result == null || resp.Trans_Result.Count == 0)
                    return null;

                // 多段翻译拼接（如多行文本会返回多条 trans_result）
                var dsts = resp.Trans_Result
                    .Select(r => r.Dst ?? "")
                    .Where(d => d.Length > 0)
                    .ToList();

                return dsts.Count > 0 ? string.Join(" ", dsts) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>MyMemory 免费 API（无需 Key，质量较低，作为回退）</summary>
        private string? TranslateViaMyMemory(string text)
        {
            try
            {
                var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=zh|en";
                var resp = _http.GetFromJsonAsync<MyMemoryResponse>(url).GetAwaiter().GetResult();
                var translated = resp?.responseData?.translatedText?.Trim('"', '\'', ' ');
                return string.IsNullOrWhiteSpace(translated) ? null : translated;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>计算 MD5 哈希（百度 API 签名用，返回小写十六进制）</summary>
        private static string ComputeMd5(string input)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>将翻译结果拆分为合法的英文单词数组</summary>
        private static List<string> SplitWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new();
            // 按空格、下划线、短横线、驼峰边界拆分
            var spaced = text.Replace('_', ' ').Replace('-', ' ');
            // 拆驼峰：在大写字母前插入空格
            for (int i = spaced.Length - 1; i > 0; i--)
            {
                if (char.IsUpper(spaced[i]) && !char.IsUpper(spaced[i - 1]))
                    spaced = spaced.Insert(i, " ");
            }
            return spaced
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(w => w.Length > 0 && w.All(IsAsciiLetterOrDigit))
                .Select(w => w.ToLowerInvariant())
                .ToList();
        }

        /// <summary>判断字符是否为 ASCII 字母或数字（a-z, A-Z, 0-9），排除中文等非英文字符</summary>
        private static bool IsAsciiLetterOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        // ── 百度翻译 API 响应 DTO ──

        private class BaiduResponse
        {
            [JsonPropertyName("from")]
            public string? From { get; set; }

            [JsonPropertyName("to")]
            public string? To { get; set; }

            [JsonPropertyName("trans_result")]
            public List<BaiduTransItem>? Trans_Result { get; set; }

            [JsonPropertyName("error_code")]
            public string? Error_Code { get; set; }

            [JsonPropertyName("error_msg")]
            public string? Error_Msg { get; set; }
        }

        private class BaiduTransItem
        {
            [JsonPropertyName("src")]
            public string? Src { get; set; }

            [JsonPropertyName("dst")]
            public string? Dst { get; set; }
        }

        // ── MyMemory API 响应 DTO ──

        private class MyMemoryResponse
        {
            [JsonPropertyName("responseData")]
            public ResponseData? responseData { get; set; }
        }

        private class ResponseData
        {
            [JsonPropertyName("translatedText")]
            public string? translatedText { get; set; }
        }
    }
}
