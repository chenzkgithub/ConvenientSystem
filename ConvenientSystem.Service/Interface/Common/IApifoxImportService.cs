using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Apifox 同步服务：配置归属当前用户，服务端保管 Access Token 并代为调用 Apifox 开放 API。
    /// </summary>
    public interface IApifoxImportService
    {
        /// <summary>读取当前用户的 Access Token 保存状态，不返回令牌内容。</summary>
        ApifoxAccessTokenStatusDto GetMyAccessTokenStatus();

        /// <summary>保存或清除当前用户的 Access Token。</summary>
        void SaveMyAccessToken(ApifoxAccessTokenSaveRequest request);

        /// <summary>将桌面端生成的 OpenAPI JSON 导入本次请求指定的 Apifox 项目。</summary>
        Task<ApifoxImportResultDto> ImportAsync(ApifoxImportRequest request, CancellationToken cancellationToken = default);
    }
}
