using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 手机端权限管理接口（手机端 App「权限管理」页专用）：权限点目录动态下发、用户权限列表、差量保存。
    /// 仅手机端 admin 令牌含 app-permission-manage 码，PC 端 token 不含该码自然 403（管理入口只在手机端）。
    /// 保存后覆盖目标用户 app 平台的会话 JTI：其旧令牌下次请求即 401，重新登录后按新权限签发（近实时生效）。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize(AppPerm.PermissionManage)]
    public class AppPermController : BaseController
    {
        private readonly IAppPermService _service;
        private readonly SessionTokenStore _sessionStore;

        public AppPermController(IAppPermService service, SessionTokenStore sessionStore)
        {
            _service = service;
            _sessionStore = sessionStore;
        }

        /// <summary>权限点目录（开关清单动态下发，新增权限点无需改手机端）。</summary>
        [HttpGet]
        public ActionResult<List<AppPermPointDto>> Catalog() => Ok(_service.GetCatalog());

        /// <summary>用户列表（含各自已开通的手机端权限码；管理员恒全量）。</summary>
        [HttpGet]
        public ActionResult<List<AppPermUserDto>> Users() => Ok(_service.GetUsers());

        /// <summary>差量保存用户手机端权限；同时使目标用户手机端会话失效（对方重新登录后按新权限进入）。</summary>
        [HttpPost]
        public IActionResult Save([FromBody] AppPermSaveDto dto)
        {
            if (dto == null || dto.UserId == Guid.Empty)
                return Ok(new { ok = false, msg = "参数错误" });

            _service.SaveUserPerms(CurrentUserId ?? Guid.Empty, dto.UserId, dto.PermCodes);

            // 近实时生效：覆盖目标用户 app 平台的会话 JTI（不能改为 Remove——
            // “无记录放行”的兼容逻辑会放行旧令牌；写入新值才能让旧令牌 JTI 校验不匹配而 401）
            _sessionStore.Set(dto.UserId, ClientPlatform.App, Guid.NewGuid().ToString("N"));
            return Ok(new { ok = true, msg = "已保存，对方下次登录生效" });
        }
    }
}
