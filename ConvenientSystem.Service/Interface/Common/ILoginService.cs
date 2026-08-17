using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 登录业务服务：默认账号回填与登录校验。
    /// </summary>
    public interface ILoginService
    {
        /// <summary>读取登录界面默认显示的账号与密码</summary>
        Task<LoginDefaultDto> GetLoginDefaultAsync();

        /// <summary>校验登录账号与密码</summary>
        Task<LoginVerifyDto> VerifyLoginAsync(LoginDto request);

        /// <summary>检查用户是否仍处于启用状态（心跳轮询用）。</summary>
        Task<LoginStatusDto> CheckStatusAsync(Guid userId);
    }
}
