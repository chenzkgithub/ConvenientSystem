using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 个人资料接口：当前登录用户查看/修改自己的资料与密码。
    /// 仅要求已登录（[Authorize]），不挂菜单权限码——任何账号都应能改自己的资料。
    /// 目标用户恒取自 JWT，不接受请求体传入，避免越权修改他人。
    /// </summary>
    [Area("Common")]
    [Authorize]
    public class ProfileController : BaseController
    {
        private readonly IProfileService _service;

        public ProfileController(IProfileService service)
        {
            _service = service;
        }

        /// <summary>当前登录用户的个人资料。</summary>
        [HttpGet]
        public async Task<ActionResult<ProfileDto>> Get()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(await _service.GetProfileAsync(userId));
        }

        /// <summary>修改显示名称。</summary>
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] ProfileSaveDto dto)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            await _service.SaveProfileAsync(userId, dto);
            return Ok();
        }

        /// <summary>修改本人密码（校验原密码）。修改成功后前端应提示重新登录。</summary>
        [HttpPost]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            await _service.ChangePasswordAsync(userId, dto);
            return Ok();
        }

        /// <summary>
        /// 取出 JWT 中的数据库用户 Id。
        /// 兜底账号（userId=Guid.Empty）允许通过，Service 层对不存在的用户返回空结果或默认值，不报 400。
        /// </summary>
        private bool TryGetDbUserId(out Guid userId, out ActionResult? error)
        {
            var id = CurrentUserId;
            if (!id.HasValue)
            {
                userId = Guid.Empty;
                error = Unauthorized();
                return false;
            }
            // 兜底账号（userId=Guid.Empty）允许通过，Service 返回空结果
            userId = id.Value;
            error = null;
            return true;
        }
    }
}
