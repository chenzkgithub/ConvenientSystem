namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 接口调试代理 DTO：调试请求由服务端 HttpClient 转发到目标地址（规避浏览器 CORS 限制），
    /// 目标服务的原始响应原样回传，前端抽屉展示状态码 / 响应头 / 响应体 / 耗时。
    /// </summary>

    /// <summary>调试请求。</summary>
    public class ApiDebugRequest
    {
        /// <summary>HTTP 方法（GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS），默认 GET。</summary>
        public string Method { get; set; } = "GET";
        /// <summary>目标接口绝对地址（仅支持 http/https）。</summary>
        public string Url { get; set; } = "";
        /// <summary>请求头键值对（Host/Content-Length/Content-Type/Transfer-Encoding/Connection 由 HttpClient 接管，忽略传入值）。</summary>
        public Dictionary<string, string>? Headers { get; set; }
        /// <summary>请求体文本；未显式指定 Content-Type 时按 application/json 发送。</summary>
        public string? Body { get; set; }
        /// <summary>超时毫秒数（钳制在 1000~120000，默认 30000）。</summary>
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>调试响应：目标服务的原始响应 + 耗时；网络层失败时 Error 有值、StatusCode 为 0。</summary>
    public class ApiDebugResponse
    {
        /// <summary>HTTP 状态码（网络层失败为 0）。</summary>
        public int StatusCode { get; set; }
        /// <summary>状态原因短语（如 OK、Bad Request）。</summary>
        public string StatusText { get; set; } = "";
        /// <summary>响应头（多值头以分号拼接，含 Content 头）。</summary>
        public Dictionary<string, string> ResponseHeaders { get; set; } = new();
        /// <summary>响应体文本（超过展示上限截断并附加说明）。</summary>
        public string Body { get; set; } = "";
        /// <summary>耗时毫秒数（含网络往返）。</summary>
        public long ElapsedMs { get; set; }
        /// <summary>网络层错误信息（连接失败、超时、DNS 解析失败等；无则空）。</summary>
        public string? Error { get; set; }
    }
}
