using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 接口调试代理：把调试请求经服务端 HttpClient 转发到目标地址并回传原始响应，
    /// 规避浏览器 CORS 限制（本机 Kestrel / 云端页面都能调试任意 http/https 接口）。
    /// 无状态，不落库；网络层异常封装进响应返回，不抛业务异常。
    /// </summary>
    public interface IApiDebugService
    {
        /// <summary>发送一次调试请求，返回目标服务的原始响应（含状态码、响应头、响应体、耗时）。</summary>
        Task<ApiDebugResponse> DebugAsync(ApiDebugRequest request);
    }
}
