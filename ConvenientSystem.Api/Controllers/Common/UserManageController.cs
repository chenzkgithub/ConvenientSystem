using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 用户管理接口：用户增删改查、启停、重置密码、分配角色。需"用户管理"权限。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("user-manage")]
    public class UserManageController : BaseController
    {
        private readonly IUserManageService _service;

        public UserManageController(IUserManageService service)
        {
            _service = service;
        }

        /// <summary>用户列表（含所属角色）。</summary>
        [HttpGet]
        public ActionResult<List<UserManageDto>> List() => Ok(_service.GetUsers());

        /// <summary>新增或更新用户。</summary>
        [HttpPost]
        public IActionResult Save([FromBody] UserSaveDto dto)
        {
            _service.SaveUser(dto);
            return Ok();
        }

        /// <summary>启用/停用用户。</summary>
        [HttpPost]
        public IActionResult SetEnabled([FromBody] SetEnabledDto dto)
        {
            _service.SetEnabled(dto);
            return Ok();
        }

        /// <summary>重置密码。</summary>
        [HttpPost]
        public IActionResult ResetPassword([FromBody] ResetPasswordDto dto)
        {
            _service.ResetPassword(dto);
            return Ok();
        }

        /// <summary>删除用户。</summary>
        [HttpPost]
        public IActionResult Delete([FromBody] Guid id)
        {
            _service.Delete(id);
            return Ok();
        }
    }
}
