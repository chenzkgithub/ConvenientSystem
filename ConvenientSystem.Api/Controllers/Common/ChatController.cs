using ConvenientSystem.Shared.Common;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
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
        private readonly IFreeSql _fsql;

        public ChatController(
            IChatService service,
            IHubContext<ChatHub> hubContext,
            OnlineUserTracker tracker,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _service = service;
            _hubContext = hubContext;
            _tracker = tracker;
            _fsql = fsql;
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

        /// <summary>按 Id 查询单条消息：用于引用块点击定位；若消息不存在或已被我方清空，则返回 404。</summary>
        [HttpGet]
        public ActionResult<ChatMessageDto> MessageById([FromQuery] long conversationId, [FromQuery] long messageId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var msg = _service.GetMessageById(userId, conversationId, messageId);
            if (msg == null) return NotFound(new { message = "原消息不存在或已被删除" });
            return Ok(msg);
        }

        /// <summary>发送消息：单聊走 PeerId；群聊 PeerId 为空，由 ConversationId 定位，推送给所有成员。</summary>
        [HttpPost]
        public async Task<ActionResult<ChatMessageDto>> Send([FromBody] ChatSendRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            if (request.MsgType is not (0 or 1))
                return BadRequest(new { message = "不支持的消息类型" });

            // 图片必须是本服务生成的路径且文件仍存在，不能把任意字符串伪装为图片消息。
            if (request.MsgType == 1 && (!TryResolveImageFile(request.Content, out var filePath, out _) || !System.IO.File.Exists(filePath)))
                return BadRequest(new { message = "图片不存在或已失效，请重新上传" });

            var msg = _service.SendMessage(userId, request.PeerId, request.ConversationId, request.Content, request.QuoteId, request.MsgType, request.Mentions);

            // 实时推送给目标用户/群全体成员
            var targetUserIds = await ResolveMessageRecipientsAsync(msg.ConversationId, userId);
            await _hubContext.Clients.Users(targetUserIds)
                .SendAsync("ReceiveMessage", msg);
            return Ok(msg);
        }

        /// <summary>创建群聊：群名必填，成员至少 1 人（创建者自动加入，总人数上限 1000）。</summary>
        [HttpPost]
        public ActionResult<ChatConversationDto> CreateGroup([FromBody] ChatGroupCreateRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var conv = _service.CreateGroup(userId, request.Name, request.MemberIds);
            return Ok(conv);
        }

        /// <summary>获取群聊成员列表（含在线状态）。</summary>
        [HttpGet]
        public ActionResult<List<ChatGroupMemberDto>> GroupMembers([FromQuery] long conversationId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var members = _service.GetGroupMembers(userId, conversationId);
            var online = _tracker.GetOnlineUserIds();
            foreach (var m in members) m.Online = online.Contains(m.UserId);
            return Ok(members);
        }

        // ===== 图片 =====

        /// <summary>图片存储根目录（Docker volume 持久化，同 WebPackage 模式）。</summary>
        private static readonly string ImageRoot =
            Environment.GetEnvironmentVariable("CHAT_IMAGE_DIR") ?? "/data/chat-images";

        /// <summary>允许的图片扩展名与对应 Content-Type。</summary>
        private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
        };

        /// <summary>聊天图片大小上限（10MB，与前端预校验一致）。</summary>
        private const long MaxImageSize = 10 * 1024 * 1024;

        /// <summary>上传聊天图片：≤10MB，扩展名 + 文件头魔数双重校验；返回相对路径（消息 Content 直接使用）。</summary>
        [HttpPost]
        [RequestSizeLimit(MaxImageSize + 1024)]
        public ActionResult<ChatImageDto> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "请选择要发送的图片" });
            if (file.Length > MaxImageSize) return BadRequest(new { message = "图片不能超过 10MB" });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!ImageTypes.TryGetValue(ext, out var contentType))
                return BadRequest(new { message = "仅支持 jpg/png/gif/webp/bmp 图片" });

            // 魔数校验：防止把可执行文件改后缀上传（至少读 12 字节覆盖全部格式头）
            var head = new byte[12];
            using (var stream = file.OpenReadStream())
            {
                var read = stream.Read(head, 0, head.Length);
                if (read < head.Length || !LooksLikeImage(head, ext))
                    return BadRequest(new { message = "文件内容不是有效的图片" });
            }

            var dir = Path.Combine(ImageRoot, TimeHelper.Now.ToString("yyyyMM"));
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(dir, fileName);
            using (var fs = System.IO.File.Create(filePath))
            {
                file.CopyTo(fs);
            }
            return Ok(new ChatImageDto { Path = $"{TimeHelper.Now:yyyyMM}/{fileName}" });
        }

        /// <summary>图片访问：Guid 文件名即访问凭证（img 标签无法携带 JWT，同微信 CDN 模式），匿名可访问但不可枚举。</summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Image([FromQuery] string f)
        {
            if (!TryResolveImageFile(f, out var filePath, out var contentType) || !System.IO.File.Exists(filePath))
                return NotFound(new { message = "图片不存在" });

            return File(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read), contentType);
        }

        /// <summary>解析并严格校验本服务生成的图片相对路径，避免路径穿越；供发送校验与读取共用。</summary>
        private static bool TryResolveImageFile(string? relativePath, out string filePath, out string contentType)
        {
            filePath = string.Empty;
            contentType = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath) || !System.Text.RegularExpressions.Regex.IsMatch(
                    relativePath, @"^\d{6}/[0-9a-f]{32}\.(jpg|jpeg|png|gif|webp|bmp)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return false;

            var ext = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!ImageTypes.TryGetValue(ext, out contentType)) return false;

            filePath = Path.Combine(ImageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }

        /// <summary>文件头魔数校验：按扩展名比对特征字节（jpg FF D8 FF / png 89 50 4E 47 / gif GIF8 / webp RIFF…WEBP / bmp BM）。</summary>
        private static bool LooksLikeImage(byte[] head, string ext) => ext switch
        {
            ".jpg" or ".jpeg" => head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            ".png" => head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
            ".gif" => head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38,
            ".webp" => head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
                        && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50,
            ".bmp" => head[0] == 0x42 && head[1] == 0x4D,
            _ => false,
        };

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

        // ===== 单方面删除 / 转发 =====

        /// <summary>清空我方聊天记录（单方面删除）：仅我方视图隐藏历史，对方不受影响；推 MessagesCleared 仅给我自己的其他登录端同步。</summary>
        [HttpPost]
        public async Task<IActionResult> ClearMessages([FromQuery] long conversationId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.ClearMessagesMySide(userId, conversationId);
            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("MessagesCleared", conversationId);
            return Ok();
        }

        /// <summary>转发消息到目标联系人：Merged=false 逐条转发 / true 合并为一条“聊天记录”卡片；转发结果逐条推送给双方。</summary>
        [HttpPost]
        public async Task<IActionResult> Forward([FromBody] ChatForwardRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var targets = new[] { userId.ToString(), request.TargetPeerId.ToString() };

            if (request.Merged)
            {
                var card = _service.ForwardMerged(userId, request.MessageIds, request.TargetPeerId);
                await _hubContext.Clients.Users(targets).SendAsync("ReceiveMessage", card);
                return Ok(new { count = 1 });
            }

            var list = _service.ForwardMessages(userId, request.MessageIds, request.TargetPeerId);
            foreach (var msg in list)
            {
                await _hubContext.Clients.Users(targets).SendAsync("ReceiveMessage", msg);
            }
            return Ok(new { count = list.Count });
        }

        /// <summary>查看合并转发记录（快照只读）：创建者或收到过该卡片的人可看。</summary>
        [HttpGet]
        public ActionResult<ChatForwardRecordDto> ForwardRecord([FromQuery] long recordId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetForwardRecord(userId, recordId));
        }

        /// <summary>解析消息接收者：单聊取对方；群聊取全体成员（含发送方自己，用于多端同步）。</summary>
        private async Task<List<string>> ResolveMessageRecipientsAsync(long conversationId, Guid senderId)
        {
            var conv = _fsql.Select<ChatConversationEntity>().Where(c => c.Id == conversationId).First();
            if (conv == null) return new List<string>();

            if (conv.ConversationType == 0)
            {
                // 单聊：自己和对方
                var peerId = _service.GetPeerId(senderId, conversationId);
                return peerId == Guid.Empty
                    ? new List<string> { senderId.ToString() }
                    : new List<string> { senderId.ToString(), peerId.ToString() };
            }

            // 群聊：全体成员
            var members = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.ConversationId == conversationId)
                .ToList(m => m.UserId);
            return members.Select(m => m.ToString()).ToList();
        }

        /// <summary>当前用户身份兜底（未登录 401；兜底账号 userId=Guid.Empty 允许通过，Service 返回空结果）。</summary>
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
