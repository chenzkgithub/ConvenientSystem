using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 锁屏业务服务：客户端配置读取与解锁密码校验。
    /// </summary>
    public interface ILockService
    {
        /// <summary>读取前端运行所需的客户端配置</summary>
        AppConfigDto GetAppConfig();

        /// <summary>校验锁屏解锁密码（校验当前登录用户的 SysUser 密码）</summary>
        Task<UnlockVerifyDto> VerifyUnlock(UnlockDto request);
    }
}
