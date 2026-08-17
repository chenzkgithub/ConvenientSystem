using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统配置控制器：读取与更新 SysConfig 表中的键值对配置。
    /// 需要 "sys-config" 权限（系统管理菜单下）。
    /// </summary>
    [Area("Common")]
    public class SysConfigController : BaseController
    {
        private readonly ISysConfigService _configService;

        public SysConfigController(ISysConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>获取全部系统配置（按分组返回）</summary>
        [HttpGet]
        [PermissionAuthorize("sys-config")]
        public ActionResult<List<SysConfigGroupDto>> GetAll()
            => Ok(_configService.GetAll());

        /// <summary>批量更新配置值</summary>
        [HttpPut]
        [PermissionAuthorize("sys-config")]
        public IActionResult UpdateBatch([FromBody] List<SysConfigUpdateDto> items)
        {
            _configService.UpdateBatch(items);
            return Ok(new { message = "配置已保存" });
        }

        /// <summary>查看敏感配置明文（需验证用户登录密码）</summary>
        [HttpPost]
        [PermissionAuthorize("sys-config")]
        public ActionResult<SysConfigRevealResult> RevealValue([FromBody] SysConfigRevealDto dto)
        {
            if (CurrentUserId is not Guid uid)
                return Unauthorized();
            var value = _configService.RevealValue(dto.ConfigKey, dto.Password, uid);
            if (value == null)
                return Ok(new SysConfigRevealResult { Ok = false });
            return Ok(new SysConfigRevealResult { Ok = true, Value = value });
        }
    }
}
