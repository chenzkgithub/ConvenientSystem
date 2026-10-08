using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 用户管理接口：用户增删改查、启停、重置密码、分配角色。需"用户管理"权限。
    /// 手机端（platform=app）改用白名单码 app-user-manage 校验（AppCode 分流），Web 端仍用菜单码。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("user-manage", AppCode = AppPerm.UserManage)]
    public class UserManageController : BaseController
    {
        private readonly IUserManageService _service;

        public UserManageController(IUserManageService service)
        {
            _service = service;
        }

        /// <summary>用户列表（含所属角色，不含头像——列表响应体瘦身，编辑弹窗按需单查）。</summary>
        [HttpGet]
        public ActionResult<List<UserManageDto>> List() => Ok(_service.GetUsers());

        /// <summary>单用户详情（含头像与角色，供编辑弹窗使用）。</summary>
        [HttpGet]
        public ActionResult<UserManageDto> Get([FromQuery] Guid id) => Ok(_service.GetUser(id));

        /// <summary>新增或更新用户。</summary>
        [HttpPost]
        [PermissionAuthorize("user-manage:add", "user-manage:edit", AppCode = AppPerm.UserManage)]
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
        [PermissionAuthorize("user-manage:reset-pwd", AppCode = AppPerm.UserManage)]
        public IActionResult ResetPassword([FromBody] ResetPasswordDto dto)
        {
            _service.ResetPassword(dto);
            return Ok();
        }

        /// <summary>删除用户（软删除）。</summary>
        [HttpPost]
        [PermissionAuthorize("user-manage:delete", AppCode = AppPerm.UserManage)]
        public IActionResult Delete([FromQuery] Guid id)
        {
            _service.Delete(id);
            return Ok();
        }

        /// <summary>恢复已删除用户（软删除还原，恢复后默认启用）。</summary>
        [HttpPost]
        [PermissionAuthorize("user-manage:restore", AppCode = AppPerm.UserManage)]
        public IActionResult Restore([FromQuery] Guid id)
        {
            _service.Restore(id);
            return Ok();
        }
    }
}
