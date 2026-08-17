using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 锁屏控制器：锁屏功能开关配置与解锁密码校验。业务逻辑见 ILockService。
    /// </summary>
    [Area("Common")]
    public class LockController : BaseController
    {
        private readonly ILockService _lockService;

        public LockController(ILockService lockService)
        {
            _lockService = lockService;
        }

        /// <summary>
        /// 读取前端运行所需的客户端配置。
        /// 目前包含：enableLock —— 是否开启锁屏功能（用户级 UserConfig，缺省 true）；
        /// lockTimeout —— 自动锁屏空闲时长秒数（用户级 UserConfig，缺省 120）。
        /// </summary>
        [HttpGet]
        public ActionResult<AppConfigDto> GetAppConfig()
            => Ok(_lockService.GetAppConfig());

        /// <summary>
        /// 校验锁屏解锁密码（界面加密）。解锁密码校验当前登录用户的 SysUser 密码，
        /// 不再使用 appsettings.json 静态密码。校验在后端完成，密码不出现在前端页面源码中。
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<UnlockVerifyDto>> VerifyUnlock([FromBody] UnlockDto request)
            => Ok(await _lockService.VerifyUnlock(request));
    }
}
