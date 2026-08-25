using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 在线用户接口：查询最近活跃的已登录用户列表。需"在线用户"权限。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("online-users")]
    public class UserOnlineController : BaseController
    {
        private readonly OnlineUserTracker _tracker;

        public UserOnlineController(OnlineUserTracker tracker)
        {
            _tracker = tracker;
        }

        /// <summary>在线用户列表（最近 6 分钟内有心跳的用户，按最后活跃时间倒序）。</summary>
        [HttpGet]
        public ActionResult<List<OnlineUserDto>> List()
        {
            var online = _tracker.GetOnline();
            return Ok(online.Select(e => new OnlineUserDto
            {
                UserId = e.UserId,
                Account = e.Account,
                DisplayName = e.DisplayName,
                Ip = e.Ip,
                LoginTime = e.LoginTime,
                LastActive = e.LastActive,
                LastHeartbeat = e.LastHeartbeat,
            }).ToList());
        }
    }
}
