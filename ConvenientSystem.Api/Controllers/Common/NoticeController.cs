using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统通知用户端接口：任何已登录用户查看通知列表/未读数并标记已读。
    /// 仅要求已登录（[Authorize]），不挂菜单权限码——通知是所有用户的公共功能。
    /// 目标用户恒取自 JWT，不接受请求体传入，避免越权代他人标记已读。
    /// </summary>
    [Area("Common")]
    [Authorize]
    public class NoticeController : BaseController
    {
        private readonly INoticeService _service;

        public NoticeController(INoticeService service)
        {
            _service = service;
        }

        /// <summary>当前用户可见的通知列表（仅启用的，含已读状态）。</summary>
        [HttpGet]
        public ActionResult<List<NoticeUserDto>> MyList()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetMyList(userId));
        }

        /// <summary>当前用户未读通知数（供顶栏铃铛角标轮询）。</summary>
        [HttpGet]
        public ActionResult<NoticeUnreadDto> UnreadCount()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(new NoticeUnreadDto { Count = _service.GetUnreadCount(userId) });
        }

        /// <summary>标记单条通知已读（幂等）。</summary>
        [HttpPost]
        public IActionResult MarkRead([FromQuery] int noticeId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.MarkRead(userId, noticeId);
            return Ok();
        }

        /// <summary>全部通知标记已读。</summary>
        [HttpPost]
        public IActionResult MarkAllRead()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.MarkAllRead(userId);
            return Ok();
        }

        /// <summary>
        /// 取出 JWT 中的数据库用户 Id（与 ProfileController 一致：兜底登录返回 400 而非 401）。
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
            if (id.Value == Guid.Empty)
            {
                userId = Guid.Empty;
                error = BadRequest(new { message = "当前会话未关联数据库账号（数据库不可用时的兜底登录），无法查看通知" });
                return false;
            }
            userId = id.Value;
            error = null;
            return true;
        }
    }
}
