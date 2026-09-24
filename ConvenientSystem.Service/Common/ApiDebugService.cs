using System.Diagnostics;
using System.Net;
using System.Text;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 接口调试代理实现：服务端 HttpClient 转发（规避浏览器 CORS）。
    /// 每次调用独立 HttpClient（不做连接池复用），避免调试目标切换时受 DNS/代理缓存干扰；
    /// 网络层异常（连接失败/超时/DNS）封装进 ApiDebugResponse.Error 返回，由前端抽屉展示。
    /// </summary>
    public class ApiDebugService : IApiDebugService
    {
        /// <summary>响应体回传上限（字符数），超出截断，避免超大响应拖垮页面。</summary>
        private const int MaxBodyChars = 200_000;

        private static readonly string[] SupportedMethods =
            { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

        /// <summary>由 HttpClient 自动管理、忽略用户传入值的头（冲突会直接导致发送失败）。</summary>
        private static readonly HashSet<string> ManagedHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Expect", "Proxy-Connection",
        };

        public async Task<ApiDebugResponse> DebugAsync(ApiDebugRequest request)
        {
            var method = (request.Method ?? "GET").Trim().ToUpperInvariant();
            if (!SupportedMethods.Contains(method))
                throw new BizException($"不支持的 HTTP 方法：{request.Method}");

            var url = (request.Url ?? "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new BizException("目标地址必须是绝对的 http/https URL");

            var timeoutMs = request.TimeoutMs <= 0 ? 30_000 : Math.Clamp(request.TimeoutMs, 1_000, 120_000);

            var response = new ApiDebugResponse();
            var sw = Stopwatch.StartNew();
            try
            {
                using var handler = new HttpClientHandler
                {
                    // 本机/内网调试不走系统代理：避免代理软件（Fiddler/Clash 等）劫持 localhost 请求
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

                using var req = new HttpRequestMessage(new HttpMethod(method), uri);
                string? contentType = null;
                if (request.Headers != null)
                {
                    foreach (var (k, v) in request.Headers)
                    {
                        if (string.IsNullOrWhiteSpace(k)) continue;
                        if (ManagedHeaders.Contains(k))
                        {
                            if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = v;
                            continue;
                        }
                        req.Headers.TryAddWithoutValidation(k, v);
                    }
                }
                
                // 根据请求类型构建请求体
                if (request.Files != null && request.Files.Count > 0)
                {
                    // multipart/form-data（带文件）
                    var multipartContent = new MultipartFormDataContent();
                    
                    // 添加表单字段
                    if (request.FormData != null)
                    {
                        foreach (var (k, v) in request.FormData)
                        {
                            if (!string.IsNullOrEmpty(k))
                            {
                                multipartContent.Add(new StringContent(v), k);
                            }
                        }
                    }
                    
                    // 添加文件
                    foreach (var file in request.Files)
                    {
                        if (string.IsNullOrEmpty(file.FieldName) || string.IsNullOrEmpty(file.Content)) continue;
                        
                        var fileBytes = Convert.FromBase64String(file.Content);
                        var byteContent = new ByteArrayContent(fileBytes);
                        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                        multipartContent.Add(byteContent, file.FieldName, file.FileName);
                    }
                    
                    req.Content = multipartContent;
                }
                else if (request.FormData != null && request.FormData.Count > 0)
                {
                    // application/x-www-form-urlencoded 或 multipart/form-data（无文件）
                    if (contentType?.Contains("multipart/form-data") == true)
                    {
                        var multipartContent = new MultipartFormDataContent();
                        foreach (var (k, v) in request.FormData)
                        {
                            if (!string.IsNullOrEmpty(k))
                            {
                                multipartContent.Add(new StringContent(v), k);
                            }
                        }
                        req.Content = multipartContent;
                    }
                    else
                    {
                        // 默认 application/x-www-form-urlencoded
                        var formContent = new FormUrlEncodedContent(request.FormData!);
                        req.Content = formContent;
                    }
                }
                else if (!string.IsNullOrEmpty(request.Body))
                {
                    // JSON 或其他文本格式
                    req.Content = new StringContent(request.Body, Encoding.UTF8, contentType ?? "application/json");
                }

                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                response.StatusCode = (int)res.StatusCode;
                response.StatusText = res.ReasonPhrase ?? "";
                foreach (var h in res.Headers)
                    response.ResponseHeaders[h.Key] = string.Join("; ", h.Value);
                foreach (var h in res.Content.Headers)
                    response.ResponseHeaders[h.Key] = string.Join("; ", h.Value);

                var body = await res.Content.ReadAsStringAsync();
                if (body.Length > MaxBodyChars)
                    body = body.Substring(0, MaxBodyChars) +
                           $"\n\n… 响应体过长已截断（共 {body.Length} 字符，仅回传前 {MaxBodyChars}）";
                response.Body = body;
            }
            catch (TaskCanceledException)
            {
                response.Error = $"请求超时（>{timeoutMs} ms），已中断";
            }
            catch (HttpRequestException ex)
            {
                response.Error = $"网络层错误：{ex.Message}";
            }
            catch (Exception ex)
            {
                response.Error = $"发送失败：{ex.Message}";
            }
            finally
            {
                sw.Stop();
                response.ElapsedMs = sw.ElapsedMilliseconds;
            }
            return response;
        }
    }
}
