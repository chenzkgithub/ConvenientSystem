using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 聊天用户端接口：任何已登录用户的公共功能（会话/消息/通讯录/屏蔽），仅 [Authorize] 不挂菜单权限码（同 NoticeController）。
    /// 目标用户恒取自 JWT，不接受请求体传入，防越权代他人标记已读/屏蔽。
    /// 实时推送（SignalR）在本层结合 IHubContext 编排：发送推 ReceiveMessage 给双方、已读推 ReadAck 给对方。
    /// </summary>
    [Area("Common")]
    [Authorize]
    public class ChatController : BaseController
    {
        private readonly IChatService _service;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly OnlineUserTracker _tracker;

        public ChatController(IChatService service, IHubContext<ChatHub> hubContext, OnlineUserTracker tracker)
        {
            _service = service;
            _hubContext = hubContext;
            _tracker = tracker;
        }

        /// <summary>我的会话列表（未隐藏，按最后消息时间倒序）。</summary>
        [HttpGet]
        public ActionResult<List<ChatConversationDto>> Conversations()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetConversations(userId));
        }

        /// <summary>通讯录：全部启用用户 + 双向屏蔽标记 + 在线状态（OnlineUserTracker 填充）。</summary>
        [HttpGet]
        public ActionResult<List<ChatContactDto>> Contacts()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var contacts = _service.GetContacts(userId);
            var online = _tracker.GetOnline().Select(e => e.UserId).ToHashSet();
            foreach (var c in contacts) c.Online = online.Contains(c.UserId);
            return Ok(contacts);
        }

        /// <summary>打开（或创建）与指定用户的会话：清我方隐藏标记，返回会话 Id 与对方信息（含屏蔽标记）。</summary>
        [HttpPost]
        public ActionResult<ChatOpenDto> Open([FromQuery] Guid peerId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.OpenConversation(userId, peerId));
        }

        /// <summary>会话历史消息（正序返回）：beforeId&gt;0 时向上翻页。</summary>
        [HttpGet]
        public ActionResult<List<ChatMessageDto>> Messages([FromQuery] long conversationId, long beforeId = 0, int limit = 50)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetMessages(userId, conversationId, beforeId, limit));
        }

        /// <summary>发送消息：双向屏蔽校验→落库→更新会话最后消息→推送给双方（发送方多端同步 + 接收方即时到账）。</summary>
        [HttpPost]
        public async Task<ActionResult<ChatMessageDto>> Send([FromBody] ChatSendRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var msg = _service.SendMessage(userId, request.PeerId, request.Content);

            // 实时推送给会话双方（发送方多端同步 + 接收方即时到账）
            await _hubContext.Clients.Users(userId.ToString(), request.PeerId.ToString())
                .SendAsync("ReceiveMessage", msg);
            return Ok(msg);
        }

        /// <summary>标记已读水位：body 为 { conversationId, messageId }；有推进才推 ReadAck 给对方（对方多端同步）。</summary>
        [HttpPost]
        public async Task<IActionResult> MarkRead([FromBody] ChatMarkReadRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var advanced = _service.MarkRead(userId, request.ConversationId, request.MessageId);
            if (!advanced) return Ok();

            var peerId = _service.GetPeerId(userId, request.ConversationId);
            if (peerId == Guid.Empty) return Ok();
            await _hubContext.Clients.User(peerId.ToString())
                .SendAsync("ReadAck", request.ConversationId, userId.ToString(), request.MessageId);
            return Ok();
        }

        /// <summary>总未读数（顶栏红点轮询）。</summary>
        [HttpGet]
        public ActionResult<ChatUnreadDto> UnreadTotal()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(new ChatUnreadDto { Count = _service.GetUnreadTotal(userId) });
        }

        /// <summary>屏蔽名单管理：屏蔽（幂等）/ 取消屏蔽（幂等）/ 我屏蔽的名单。</summary>
        [HttpPost]
        public IActionResult Block([FromQuery] Guid peerId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.BlockUser(userId, peerId);
            return Ok();
        }

        [HttpPost]
        public IActionResult Unblock([FromQuery] Guid peerId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.UnblockUser(userId, peerId);
            return Ok();
        }

        [HttpGet]
        public ActionResult<List<ChatBlockDto>> Blocks()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetBlocks(userId));
        }

        /// <summary>隐藏（删除）会话：仅我方视角，来新消息自动恢复。</summary>
        [HttpPost]
        public IActionResult Hide([FromQuery] long conversationId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.HideConversation(userId, conversationId);
            return Ok();
        }

        /// <summary>设置会话免打扰（仍计未读，前端不提醒）。</summary>
        [HttpPost]
        public IActionResult SetMuted([FromQuery] long conversationId, [FromQuery] bool muted)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.SetMuted(userId, conversationId, muted);
            return Ok();
        }

        /// <summary>当前用户身份兆底（未登录 401；兜底账号 userId=Guid.Empty 允许通过，Service 返回空结果）。</summary>
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
