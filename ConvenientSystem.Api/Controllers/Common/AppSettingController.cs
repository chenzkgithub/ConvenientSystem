using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 手机端用户设置接口（账号级 KV）：登录后拉全量回本地实现设置跨设备同步
    /// （聊天背景图/主题/默认彩种等）；单项幂等保存，value 空串=恢复默认（删除该行）。
    /// 挂 app-login 基础权限（手机端登录即有，任何能登录的账号都有个人设置）；
    /// PC 端 token 平台不匹配自然 403，仅手机端可用。
    /// 目标用户恒取自 JWT，不接受请求体传入，防越权改他人设置。
    /// </summary>
    [Area("Common")]
    [Authorize]
    [PermissionAuthorize(AppPerm.Login, Platform = ClientPlatform.App)]
    public class AppSettingController : BaseController
    {
        private readonly IAppSettingService _service;

        public AppSettingController(IAppSettingService service)
        {
            _service = service;
        }

        /// <summary>我的全部设置（键值对；登录后/换设备后由手机端拉取写回本地）。</summary>
        [HttpGet]
        public ActionResult<Dictionary<string, string>> All()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetAll(userId));
        }

        /// <summary>单项幂等保存：key/value 走 query（fire-and-forget 场景 GET 语义更顺但改状态仍用 POST）；value 空串=恢复默认。</summary>
        [HttpPost]
        public IActionResult Save([FromQuery] string key, [FromQuery] string value)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            try
            {
                _service.Save(userId, key, value ?? string.Empty);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            return Ok();
        }

        /// <summary>从 JWT 取当前用户 DB Id（ChatController 同款）；缺失时返回 401。</summary>
        private bool TryGetDbUserId(out Guid userId, out ActionResult? error)
        {
            var id = CurrentUserId;
            if (!id.HasValue)
            {
                userId = Guid.Empty;
                error = Unauthorized();
                return false;
            }
            userId = id.Value;
            error = null;
            return true;
        }
    }
}
