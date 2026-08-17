using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 用户个人配置控制器：读取与更新当前登录用户的个性化配置。
    /// 任何已登录用户均可访问（无权限码限制），仅可管理自己的配置。
    /// </summary>
    [Area("Common")]
    public class UserConfigController : BaseController
    {
        private readonly IUserConfigService _userConfigService;

        public UserConfigController(IUserConfigService userConfigService)
        {
            _userConfigService = userConfigService;
        }

        /// <summary>获取当前用户的配置（合并全局默认值 + 用户覆盖值，按分组返回）</summary>
        [HttpGet]
        public ActionResult<List<UserConfigGroupDto>> GetMyConfig()
            => Ok(_userConfigService.GetMyConfig());

        /// <summary>批量更新当前用户配置值</summary>
        [HttpPut]
        public IActionResult UpdateBatch([FromBody] List<UserConfigSaveDto> items)
        {
            _userConfigService.UpdateBatch(items ?? new());
            return Ok(new { message = "个人配置已保存" });
        }
    }
}
