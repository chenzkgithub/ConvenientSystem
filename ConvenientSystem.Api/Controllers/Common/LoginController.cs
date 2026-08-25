using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 登录控制器：登录界面默认账号回填、登录校验与心跳状态检查。业务逻辑见 ILoginService。
    /// </summary>
    [Area("Common")]
    public class LoginController : BaseController
    {
        private readonly ILoginService _loginService;
        private readonly OnlineUserTracker _tracker;
        private readonly SessionTokenStore _sessionStore;

        public LoginController(ILoginService loginService, OnlineUserTracker tracker, SessionTokenStore sessionStore)
        {
            _loginService = loginService;
            _tracker = tracker;
            _sessionStore = sessionStore;
        }

        /// <summary>
        /// 读取登录界面默认显示的账号与密码（SysUser 表中第一个启用账号）。
        /// 前端登录界面加载时调用，用于默认回填输入框。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<LoginDefaultDto>> GetLoginDefault()
            => Ok(await _loginService.GetLoginDefaultAsync());

        /// <summary>
        /// 校验登录账号与密码。
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<LoginVerifyDto>> VerifyLogin([FromBody] LoginDto request)
        {
            var result = await _loginService.VerifyLoginAsync(request);
            // 登录成功：将用户写入在线追踪器（登录时刻作为初始入场时间），并注册 JTI 实现挤号。
            // 登录请求本身不携带 JWT，CurrentUserId 为空，故用户信息取自登录结果。
            if (result.Ok && result.Token != null && result.UserId != Guid.Empty)
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                _tracker.Track(result.UserId,
                    result.Account ?? request?.account ?? string.Empty,
                    result.DisplayName,
                    ip);

                // 挤号：注册新令牌的 JTI，覆盖该用户之前的会话。
                // 旧令牌携带的 JTI 与存储不匹配，下次请求时被中间件拒绝。
                var jti = new JwtSecurityTokenHandler().ReadJwtToken(result.Token).Id;
                if (!string.IsNullOrEmpty(jti))
                    _sessionStore.Set(result.UserId, jti);
            }
            return Ok(result);
        }

        /// <summary>
        /// 心跳检查：前端已登录用户每 10 秒轮询一次。
        /// - 返回 { enabled: true } 表示账号仍有效，同时更新在线足迹。
        /// - 返回 { enabled: false } 表示账号已被停用，前端收到后应展示提示并退出登录。
        /// - lastActivity 参数为前端记录的用户最后真实操作时间（ISO 8601），用于更新 LastActive。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<LoginStatusDto>> CheckStatus([FromQuery] string? lastActivity = null)
        {
            // 未登录直接返回 401（前端已登录的情况下不会出现）。
            if (User?.Identity?.IsAuthenticated != true) return Unauthorized();

            var userId = CurrentUserId;
            if (!userId.HasValue) return Unauthorized();

            var status = await _loginService.CheckStatusAsync(userId.Value);

            if (status.Enabled)
            {
                // 更新在线足迹（IP 可能随起发更新）。
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                var account = User.FindFirst(ConvenientSystem.Shared.Common.Security.JwtHelper.AccountClaim)?.Value ?? string.Empty;
                var displayName = User.FindFirst(ConvenientSystem.Shared.Common.Security.JwtHelper.DisplayNameClaim)?.Value;

                DateTime? lastActiveAt = null;
                if (!string.IsNullOrWhiteSpace(lastActivity)
                    && DateTime.TryParse(lastActivity, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    lastActiveAt = parsed;

                _tracker.Track(userId.Value, account, displayName, ip, lastActiveAt);
            }

            return Ok(status);
        }

        /// <summary>
        /// 退出登录：从在线追踪器中移除当前用户。
        /// </summary>
        [HttpPost]
        public ActionResult Logout()
        {
            if (User?.Identity?.IsAuthenticated == true && CurrentUserId.HasValue)
            {
                _tracker.Remove(CurrentUserId.Value);
                _sessionStore.Remove(CurrentUserId.Value);
            }
            return Ok(new { ok = true });
        }
    }
}
