using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统通知用户端接口：任何已登录用户查看通知列表/未读数并标记已读。
    /// 仅要求已登录（[Authorize]），不挂菜单权限码——通知是所有用户的公共功能。
    /// 目标用户恒取自 JWT，不接受请求体传入，避免越权代他人标记已读。
/// 新通知创建后通过 ChatHub 推 NoticeCreated 广播，前端秒级刷新铃铛/弹卡片。
    /// </summary>
    [Area("Common")]
    [Authorize]
    public class NoticeController : BaseController
    {
        private readonly INoticeService _service;
        private readonly IHubContext<ChatHub> _hubContext;

        public NoticeController(INoticeService service, IHubContext<ChatHub> hubContext)
        {
            _service = service;
            _hubContext = hubContext;
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
        /// 构建/部署/版本发布通知：任何已登录用户可调用，创建一条系统通知（level=2 重要，前端 NoticeAlert 弹右上角卡片，不触发外部推送）。
        /// 桌面端构建服务运行在本机，无法直接访问 INoticeService，故由前端检测到终态后代理调用。
        /// 默认仅通知当前操作人（定向用户 + Clients.User 推送，不打扰其他在线用户）；
        /// broadcast=true 时全员广播（版本包发布用），无定向记录全员可见，禁用用户登录不进来等效于仅启用用户。
        /// 创建后推 NoticeCreated（无参数），前端秒级刷新铃铛/弹卡片。
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BuildNotify([FromBody] BuildNotifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Title)) return BadRequest(new { message = "标题不能为空" });

            var userId = CurrentUserId;
            var broadcast = request.Broadcast || !userId.HasValue || userId.Value == Guid.Empty; // 取不到操作人时降级全员，保底不丢通知
            _service.CreateSystemNotice(
                request.Title.Trim(),
                (request.Content ?? string.Empty).Trim(),
                level: 2,
                targetUserId: broadcast ? null : userId);

            if (broadcast)
                await _hubContext.Clients.All.SendAsync("NoticeCreated");
            else
                await _hubContext.Clients.User(userId!.Value.ToString()).SendAsync("NoticeCreated");
            return Ok();
        }

        /// <summary>
        /// 取出 JWT 中的数据库用户 Id。兜底账号（userId=Guid.Empty）允许通过，
        /// Service 层对不存在的用户返回空结果（0 条通知、空列表），不报 400。
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

/// <summary>构建/部署/版本发布通知请求体；Broadcast=true 时全员广播（默认仅通知当前操作人）。</summary>
public sealed class BuildNotifyRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>是否全员广播：false=仅当前操作人可见（构建/部署/流水线完成），true=全员可见（版本包发布）。</summary>
    public bool Broadcast { get; set; }
}
