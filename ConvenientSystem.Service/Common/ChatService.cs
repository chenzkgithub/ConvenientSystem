using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 即时聊天业务服务实现：企业通讯录模式单聊。
    /// 会话用双向归一 UserKey 定位（两个 Guid 的 N 格式排序后拼接，A→B 与 B→A 同一会话）；
    /// 未读数基于成员已读水位（ReadMessageId）计算：水位之后且非我发送的消息数。
    /// 实时推送由 Api 层 ChatController 结合 IHubContext&lt;ChatHub&gt; 编排，本层只负责落库与查询。
    /// </summary>
    public class ChatService : IChatService
    {
        /// <summary>历史消息单页条数（向上翻页默认值，上限 100）。</summary>
        private const int MessagePageSize = 50;

        /// <summary>消息正文长度上限（与 ChatMessage.Content 列宽一致）。</summary>
        private const int MaxMessageLength = 4000;

        /// <summary>会话列表最后一条消息预览长度（与 ChatConversation.LastMessageText 列宽一致）。</summary>
        private const int PreviewLength = 200;

        private readonly IFreeSql _fsql;

        public ChatService([FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        // ===== UserKey 双向归一 =====

        /// <summary>两个用户 Guid 的 N 格式（32 位十六进制）排序后拼接：A→B 与 B→A 得到同一键。</summary>
        private static string BuildUserKey(Guid a, Guid b)
        {
            var x = a.ToString("N");
            var y = b.ToString("N");
            return string.CompareOrdinal(x, y) <= 0 ? x + y : y + x;
        }

        /// <summary>从 UserKey 解析出相对 myId 的对方 Id。</summary>
        private static Guid ParsePeerFromKey(string userKey, Guid myId)
        {
            var first = userKey[..32];
            var second = userKey.Substring(32, 32);
            return first == myId.ToString("N") ? Guid.Parse(second) : Guid.Parse(first);
        }

        // ===== 用户信息批量查询 =====

        /// <summary>用户简要信息（通讯录 / 会话对方 / 消息发送者共用）。</summary>
        private sealed record UserInfo(Guid Id, string Account, string? DisplayName, string? Avatar);

        private Dictionary<Guid, UserInfo> GetUserInfoMap(List<Guid> ids)
        {
            var query = ids.Where(i => i != Guid.Empty).Distinct().ToList();
            if (query.Count == 0) return new Dictionary<Guid, UserInfo>();
            return _fsql.Select<SysUserEntity>()
                .Where(u => query.Contains(u.Id))
                .ToList(u => new UserInfo(u.Id, u.Account, u.DisplayName, u.Avatar))
                .ToDictionary(u => u.Id);
        }

        /// <summary>查询我与一组用户之间的双向屏蔽标记。</summary>
        private (HashSet<Guid> BlockedByMe, HashSet<Guid> BlockedMe) GetBlockFlags(Guid userId, List<Guid> peerIds)
        {
            if (peerIds.Count == 0) return (new HashSet<Guid>(), new HashSet<Guid>());
            var records = _fsql.Select<ChatBlockListEntity>()
                .Where(b => (b.UserId == userId && peerIds.Contains(b.BlockedUserId))
                            || (b.BlockedUserId == userId && peerIds.Contains(b.UserId)))
                .ToList();
            var byMe = records.Where(r => r.UserId == userId).Select(r => r.BlockedUserId).ToHashSet();
            var me = records.Where(r => r.BlockedUserId == userId).Select(r => r.UserId).ToHashSet();
            return (byMe, me);
        }

        // ===== 会话 =====

        /// <summary>我的会话列表（未隐藏的，按最后消息时间倒序）。</summary>
        public List<ChatConversationDto> GetConversations(Guid userId)
        {
            var members = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.UserId == userId)
                .ToList();
            if (members.Count == 0) return new List<ChatConversationDto>();

            var convIds = members.Select(m => m.ConversationId).ToList();
            var convs = _fsql.Select<ChatConversationEntity>()
                .Where(c => convIds.Contains(c.Id))
                .ToList()
                .ToDictionary(c => c.Id);

            var visible = members.Where(m => !m.Hidden && convs.ContainsKey(m.ConversationId)).ToList();
            if (visible.Count == 0) return new List<ChatConversationDto>();

            var peerIds = visible.Select(m => ParsePeerFromKey(convs[m.ConversationId].UserKey, userId)).Distinct().ToList();
            var peerMap = GetUserInfoMap(peerIds);
            var (blockedByMe, blockedMe) = GetBlockFlags(userId, peerIds);

            var result = new List<ChatConversationDto>();
            foreach (var m in visible)
            {
                var conv = convs[m.ConversationId];
                var peerId = ParsePeerFromKey(conv.UserKey, userId);
                var peer = peerMap.GetValueOrDefault(peerId);

                // 未读数：仅当最后一条非我发送且晚于水位时才精确统计
                //（绝大多数会话为 0，先按会话冗余字段过滤，避免对全部会话逐个 count）
                var unread = 0;
                if (conv.LastSenderId.HasValue && conv.LastSenderId != userId && conv.LastMessageId > m.ReadMessageId)
                    unread = (int)_fsql.Select<ChatMessageEntity>()
                        .Where(msg => msg.ConversationId == conv.Id && msg.Id > m.ReadMessageId && msg.SenderId != userId)
                        .Count();

                result.Add(new ChatConversationDto
                {
                    ConversationId = conv.Id,
                    PeerId = peerId,
                    PeerAccount = peer?.Account ?? peerId.ToString(),
                    PeerDisplayName = peer?.DisplayName,
                    PeerAvatar = peer?.Avatar,
                    LastMessageText = conv.LastMessageText,
                    LastMessageTime = conv.LastMessageTime,
                    LastFromMe = conv.LastSenderId == userId,
                    UnreadCount = unread,
                    Muted = m.Muted,
                    BlockedByMe = blockedByMe.Contains(peerId),
                    BlockedMe = blockedMe.Contains(peerId),
                });
            }

            return result
                .OrderByDescending(c => c.LastMessageTime ?? DateTime.MinValue)
                .ToList();
        }

        /// <summary>打开（或创建）与指定用户的会话：清我方隐藏标记。屏蔽双方仍可查看历史。</summary>
        public ChatOpenDto OpenConversation(Guid userId, Guid peerId)
        {
            if (peerId == userId) throw new BadRequestException("不能与自己发起会话");
            var peer = _fsql.Select<SysUserEntity>()
                .Where(u => u.Id == peerId && u.Enabled && !u.IsDeleted)
                .First();
            if (peer == null) throw new NotFoundException("对方用户不存在或已停用");

            var convId = GetOrCreateConversation(userId, peerId);

            // 打开会话即恢复显示（若曾被删除/隐藏）
            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.Hidden, false)
                .Where(m => m.ConversationId == convId && m.UserId == userId)
                .ExecuteAffrows();

            var (blockedByMe, blockedMe) = GetBlockFlags(userId, new List<Guid> { peerId });

            // 查询对方成员记录的已读水位（0=尚未读过）
            var peerReadMsgId = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.ConversationId == convId && m.UserId == peerId)
                .First(m => m.ReadMessageId);

            return new ChatOpenDto
            {
                ConversationId = convId,
                PeerId = peer.Id,
                PeerAccount = peer.Account,
                PeerDisplayName = peer.DisplayName,
                PeerAvatar = peer.Avatar,
                BlockedByMe = blockedByMe.Contains(peerId),
                BlockedMe = blockedMe.Contains(peerId),
                PeerReadMessageId = peerReadMsgId,
            };
        }

        /// <summary>按 UserKey 定位会话；不存在则创建会话与两条成员记录（并发下 UQ 冲突时重查）。</summary>
        private long GetOrCreateConversation(Guid userId, Guid peerId)
        {
            var key = BuildUserKey(userId, peerId);
            var existing = _fsql.Select<ChatConversationEntity>()
                .Where(c => c.UserKey == key)
                .First(c => c.Id);
            if (existing > 0) return existing;

            try
            {
                var newId = _fsql.Insert(new ChatConversationEntity
                {
                    UserKey = key,
                    CreateTime = DateTime.Now,
                }).ExecuteIdentity();

                _fsql.Insert(new List<ChatConversationMemberEntity>
                {
                    new() { ConversationId = newId, UserId = userId, CreateTime = DateTime.Now },
                    new() { ConversationId = newId, UserId = peerId, CreateTime = DateTime.Now },
                }).ExecuteAffrows();
                return newId;
            }
            catch
            {
                // 并发下对方已抢先创建同一会话（UQ 冲突）：重查返回同一会话
                var raced = _fsql.Select<ChatConversationEntity>()
                    .Where(c => c.UserKey == key)
                    .First(c => c.Id);
                if (raced > 0) return raced;
                throw;
            }
        }

        /// <summary>校验会话成员身份（越权访问他人会话直接 404 处理）。</summary>
        private ChatConversationMemberEntity EnsureMember(Guid userId, long conversationId)
        {
            var member = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.ConversationId == conversationId && m.UserId == userId)
                .First();
            if (member == null) throw new NotFoundException("会话不存在或你不在该会话中");
            return member;
        }

        // ===== 通讯录与屏蔽 =====

        /// <summary>通讯录：全部启用用户（不含自己）+ 双向屏蔽标记（在线状态由 Api 层填充）。</summary>
        public List<ChatContactDto> GetContacts(Guid userId)
        {
            var users = _fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled && !u.IsDeleted && u.Id != userId)
                .OrderBy(u => u.Account)
                .ToList(u => new UserInfo(u.Id, u.Account, u.DisplayName, u.Avatar));
            if (users.Count == 0) return new List<ChatContactDto>();

            var (blockedByMe, blockedMe) = GetBlockFlags(userId, users.Select(u => u.Id).ToList());

            return users.Select(u => new ChatContactDto
            {
                UserId = u.Id,
                Account = u.Account,
                DisplayName = u.DisplayName,
                Avatar = u.Avatar,
                Online = false,
                BlockedByMe = blockedByMe.Contains(u.Id),
                BlockedMe = blockedMe.Contains(u.Id),
            }).ToList();
        }

        /// <summary>屏蔽用户（幂等；不能屏蔽自己）。</summary>
        public void BlockUser(Guid userId, Guid peerId)
        {
            if (peerId == userId) throw new BadRequestException("不能屏蔽自己");
            var peerExists = _fsql.Select<SysUserEntity>()
                .Where(u => u.Id == peerId && u.Enabled && !u.IsDeleted)
                .Any();
            if (!peerExists) throw new NotFoundException("对方用户不存在或已停用");

            var exists = _fsql.Select<ChatBlockListEntity>()
                .Where(b => b.UserId == userId && b.BlockedUserId == peerId)
                .Any();
            if (!exists)
                _fsql.Insert(new ChatBlockListEntity
                {
                    UserId = userId,
                    BlockedUserId = peerId,
                    CreateTime = DateTime.Now,
                }).ExecuteAffrows();
        }

        /// <summary>取消屏蔽（幂等）。</summary>
        public void UnblockUser(Guid userId, Guid peerId)
        {
            _fsql.Delete<ChatBlockListEntity>()
                .Where(b => b.UserId == userId && b.BlockedUserId == peerId)
                .ExecuteAffrows();
        }

        /// <summary>我屏蔽的名单（含用户显示信息）。</summary>
        public List<ChatBlockDto> GetBlocks(Guid userId)
        {
            var blocks = _fsql.Select<ChatBlockListEntity>()
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreateTime)
                .ToList();
            if (blocks.Count == 0) return new List<ChatBlockDto>();

            var map = GetUserInfoMap(blocks.Select(b => b.BlockedUserId).ToList());
            return blocks.Select(b =>
            {
                var u = map.GetValueOrDefault(b.BlockedUserId);
                return new ChatBlockDto
                {
                    UserId = b.BlockedUserId,
                    Account = u?.Account ?? b.BlockedUserId.ToString(),
                    DisplayName = u?.DisplayName,
                    Avatar = u?.Avatar,
                    CreateTime = b.CreateTime,
                };
            }).ToList();
        }

        // ===== 消息 =====

        /// <summary>会话历史消息（正序返回）：beforeId&gt;0 时取该 Id 之前的一页（向上翻页）。</summary>
        public List<ChatMessageDto> GetMessages(Guid userId, long conversationId, long beforeId, int limit)
        {
            EnsureMember(userId, conversationId);
            var take = limit is >= 1 and <= 100 ? limit : MessagePageSize;

            var messages = _fsql.Select<ChatMessageEntity>()
                .Where(m => m.ConversationId == conversationId)
                .WhereIf(beforeId > 0, m => m.Id < beforeId)
                .OrderByDescending(m => m.Id)
                .Take(take)
                .ToList();
            messages.Reverse(); // 倒序取页后反转为时间正序

            if (messages.Count == 0) return new List<ChatMessageDto>();

            var senderMap = GetUserInfoMap(messages.Select(m => m.SenderId).ToList());

            return messages.Select(m =>
            {
                var s = senderMap.GetValueOrDefault(m.SenderId);
                return new ChatMessageDto
                {
                    Id = m.Id,
                    ConversationId = m.ConversationId,
                    SenderId = m.SenderId,
                    SenderAccount = s?.Account ?? m.SenderId.ToString(),
                    SenderDisplayName = s?.DisplayName,
                    Content = m.Content,
                    CreateTime = m.CreateTime,
                };
            }).ToList();
        }

        /// <summary>发送消息：双向屏蔽校验 → 落库 → 更新会话最后消息 → 双方取消隐藏。</summary>
        public ChatMessageDto SendMessage(Guid userId, Guid peerId, string content)
        {
            var text = (content ?? string.Empty).Trim();
            if (text.Length == 0) throw new BadRequestException("消息内容不能为空");
            if (text.Length > MaxMessageLength)
                throw new BadRequestException($"消息长度不能超过 {MaxMessageLength} 字");

            if (peerId == userId) throw new BadRequestException("不能给自己发送消息");
            var peerExists = _fsql.Select<SysUserEntity>()
                .Where(u => u.Id == peerId && u.Enabled && !u.IsDeleted)
                .Any();
            if (!peerExists) throw new NotFoundException("对方用户不存在或已停用");

            // 双向屏蔽即拒收：透明提示而非静默投递（避免"发出去了对方却看不到"的误解）
            var (blockedByMe, blockedMe) = GetBlockFlags(userId, new List<Guid> { peerId });
            if (blockedByMe.Contains(peerId))
                throw new BadRequestException("你已屏蔽对方，请先取消屏蔽后再发送消息");
            if (blockedMe.Contains(peerId))
                throw new BadRequestException("对方暂无法接收你的消息");

            var convId = GetOrCreateConversation(userId, peerId);
            var now = DateTime.Now;
            var preview = text.Length > PreviewLength ? text[..PreviewLength] : text;

            long messageId = 0;
            _fsql.Transaction(() =>
            {
                messageId = _fsql.Insert(new ChatMessageEntity
                {
                    ConversationId = convId,
                    SenderId = userId,
                    Content = text,
                    CreateTime = now,
                }).ExecuteIdentity();

                _fsql.Update<ChatConversationEntity>()
                    .Set(c => c.LastMessageId, messageId)
                    .Set(c => c.LastSenderId, userId)
                    .Set(c => c.LastMessageTime, now)
                    .Set(c => c.LastMessageText, preview)
                    .Where(c => c.Id == convId)
                    .ExecuteAffrows();

                // 双方取消隐藏：删除过的会话收到新消息自动恢复显示
                _fsql.Update<ChatConversationMemberEntity>()
                    .Set(m => m.Hidden, false)
                    .Where(m => m.ConversationId == convId)
                    .ExecuteAffrows();
            });

            var me = _fsql.Select<SysUserEntity>().Where(u => u.Id == userId).First();
            return new ChatMessageDto
            {
                Id = messageId,
                ConversationId = convId,
                SenderId = userId,
                SenderAccount = me?.Account ?? string.Empty,
                SenderDisplayName = me?.DisplayName,
                Content = text,
                CreateTime = now,
            };
        }

        /// <summary>推进会话已读水位到指定消息（只进不退；校验消息属于该会话）。返回是否发生推进。</summary>
        public bool MarkRead(Guid userId, long conversationId, long messageId)
        {
            var member = EnsureMember(userId, conversationId);
            if (messageId <= member.ReadMessageId) return false;

            // 校验消息确实属于该会话，防止把水位推到未来消息导致新消息不计未读
            var exists = _fsql.Select<ChatMessageEntity>()
                .Where(m => m.Id == messageId && m.ConversationId == conversationId)
                .Any();
            if (!exists) throw new BadRequestException("消息不存在或不属于该会话");

            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.ReadMessageId, messageId)
                .Where(m => m.Id == member.Id)
                .ExecuteAffrows();
            return true;
        }

        /// <summary>取会话对方的用户 Id（已读回执推送定向用；非会话成员时返回 Guid.Empty）。</summary>
        public Guid GetPeerId(Guid userId, long conversationId)
        {
            var isMember = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.ConversationId == conversationId && m.UserId == userId)
                .Any();
            if (!isMember) return Guid.Empty;

            var conv = _fsql.Select<ChatConversationEntity>()
                .Where(c => c.Id == conversationId)
                .First();
            if (conv == null || string.IsNullOrEmpty(conv.UserKey)) return Guid.Empty;

            try
            {
                return ParsePeerFromKey(conv.UserKey, userId);
            }
            catch
            {
                return Guid.Empty; // UserKey 格式异常（历史脏数据）：不拖垮已读回执推送
            }
        }

        /// <summary>总未读数（所有未隐藏会话中，水位之后且非我发送的消息数；顶栏红点轮询）。</summary>
        public int GetUnreadTotal(Guid userId)
        {
            var members = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.UserId == userId)
                .ToList();
            if (members.Count == 0) return 0;

            var convIds = members.Select(m => m.ConversationId).ToList();
            var convs = _fsql.Select<ChatConversationEntity>()
                .Where(c => convIds.Contains(c.Id))
                .ToList()
                .ToDictionary(c => c.Id);

            // 先按会话冗余字段过滤出"可能有未读"的会话（隐藏/最后是我发的直接跳过），再逐会话精确统计
            var total = 0;
            foreach (var m in members)
            {
                if (m.Hidden || !convs.TryGetValue(m.ConversationId, out var conv)) continue;
                if (conv.LastSenderId != userId && conv.LastMessageId > m.ReadMessageId)
                    total += (int)_fsql.Select<ChatMessageEntity>()
                        .Where(msg => msg.ConversationId == conv.Id && msg.Id > m.ReadMessageId && msg.SenderId != userId)
                        .Count();
            }
            return total;
        }

        // ===== 会话本地设置 =====

        /// <summary>隐藏（删除）会话：仅我方视角隐藏，收到新消息自动恢复显示。</summary>
        public void HideConversation(Guid userId, long conversationId)
        {
            EnsureMember(userId, conversationId);
            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.Hidden, true)
                .Where(m => m.ConversationId == conversationId && m.UserId == userId)
                .ExecuteAffrows();
        }

        /// <summary>设置会话免打扰（仍计未读，前端不提醒）。</summary>
        public void SetMuted(Guid userId, long conversationId, bool muted)
        {
            EnsureMember(userId, conversationId);
            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.Muted, muted)
                .Where(m => m.ConversationId == conversationId && m.UserId == userId)
                .ExecuteAffrows();
        }
    }
}
