using System.Text.Json;
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

            // 单聊：从 UserKey 解析对方 Id；群聊：无 PeerId
            var singleConvIds = visible.Where(m => convs[m.ConversationId].ConversationType == 0).Select(m => m.ConversationId).ToList();
            var peerIds = singleConvIds.Select(id => ParsePeerFromKey(convs[id].UserKey, userId)).Distinct().ToList();
            var peerMap = GetUserInfoMap(peerIds);
            var (blockedByMe, blockedMe) = GetBlockFlags(userId, peerIds);

            // 群聊：统计每个会话的成员数
            var groupConvIds = visible.Where(m => convs[m.ConversationId].ConversationType == 1).Select(m => m.ConversationId).ToList();
            var memberCountMap = groupConvIds.Count == 0
                ? new Dictionary<long, int>()
                : _fsql.Select<ChatConversationMemberEntity>()
                    .Where(mm => groupConvIds.Contains(mm.ConversationId))
                    .GroupBy(mm => mm.ConversationId)
                    .ToList(g => new { ConvId = g.Key, Count = g.Count() })
                    .ToDictionary(x => x.ConvId, x => x.Count);

            // 用最后一条实体实时生成预览：兼容首版图片曾被错误保存为 MsgType=0 的历史数据。
            var lastIds = visible.Select(m => convs[m.ConversationId].LastMessageId).Where(id => id > 0).Distinct().ToList();
            var lastMessageMap = lastIds.Count == 0
                ? new Dictionary<long, ChatMessageEntity>()
                : _fsql.Select<ChatMessageEntity>().Where(msg => lastIds.Contains(msg.Id)).ToList().ToDictionary(msg => msg.Id);

            var result = new List<ChatConversationDto>();
            foreach (var m in visible)
            {
                var conv = convs[m.ConversationId];
                var isGroup = conv.ConversationType == 1;
                var peerId = isGroup ? Guid.Empty : ParsePeerFromKey(conv.UserKey, userId);
                var peer = isGroup ? null : peerMap.GetValueOrDefault(peerId);

                // 我方已单方面删除到该消息：预览/时间置空（对方不受影响），会话排到列表底部
                var cleared = conv.LastMessageId > 0 && conv.LastMessageId <= m.ClearBeforeMessageId;

                // 未读数：仅当最后一条非我发送、晚于水位且未被我删除时才精确统计
                //（绝大多数会话为 0，先按会话冗余字段过滤，避免对全部会话逐个 count）
                var unread = 0;
                if (!cleared && conv.LastSenderId.HasValue && conv.LastSenderId != userId && conv.LastMessageId > m.ReadMessageId)
                    unread = (int)_fsql.Select<ChatMessageEntity>()
                        .Where(msg => msg.ConversationId == conv.Id && msg.Id > m.ReadMessageId && msg.Id > m.ClearBeforeMessageId && msg.SenderId != userId)
                        .Count();

                result.Add(new ChatConversationDto
                {
                    ConversationId = conv.Id,
                    ConversationType = conv.ConversationType,
                    PeerId = peerId,
                    PeerAccount = peer?.Account ?? peerId.ToString(),
                    PeerDisplayName = isGroup ? conv.GroupName : peer?.DisplayName,
                    PeerAvatar = isGroup ? conv.Avatar : peer?.Avatar,
                    GroupName = isGroup ? conv.GroupName : null,
                    MemberCount = isGroup ? memberCountMap.GetValueOrDefault(conv.Id, 1) : 2,
                    LastMessageText = cleared ? null : (lastMessageMap.TryGetValue(conv.LastMessageId, out var last)
                        ? TypePreview(last.MsgType, last.Content)
                        : conv.LastMessageText),
                    LastMessageTime = cleared ? null : conv.LastMessageTime,
                    LastFromMe = cleared ? false : conv.LastSenderId == userId,
                    UnreadCount = unread,
                    Muted = m.Muted,
                    BlockedByMe = !isGroup && blockedByMe.Contains(peerId),
                    BlockedMe = !isGroup && blockedMe.Contains(peerId),
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

        /// <summary>会话历史消息（正序返回）：校验成员身份；beforeId&gt;0 时取该 Id 之前的一页（向上翻页）；我方删除水位之前的消息不返回。</summary>
        public List<ChatMessageDto> GetMessages(Guid userId, long conversationId, long beforeId, int limit)
        {
            var member = EnsureMember(userId, conversationId);
            var take = limit is >= 1 and <= 100 ? limit : MessagePageSize;

            var messages = _fsql.Select<ChatMessageEntity>()
                .Where(m => m.ConversationId == conversationId)
                .WhereIf(beforeId > 0, m => m.Id < beforeId)
                // 单方面删除水位：仅过滤我方视图，对方（水位 0）不受影响
                .WhereIf(member.ClearBeforeMessageId > 0, m => m.Id > member.ClearBeforeMessageId)
                .OrderByDescending(m => m.Id)
                .Take(take)
                .ToList();
            messages.Reverse(); // 倒序取页后反转为时间正序

            if (messages.Count == 0) return new List<ChatMessageDto>();

            // 发送者信息：消息发送者 + 引用消息发送者一并批量查
            var senderIds = messages.Select(m => m.SenderId)
                .Concat(messages.Where(m => m.QuoteSenderId.HasValue).Select(m => m.QuoteSenderId!.Value))
                .Distinct().ToList();
            var senderMap = GetUserInfoMap(senderIds);

            return messages.Select(m =>
            {
                var s = senderMap.GetValueOrDefault(m.SenderId);
                var qs = m.QuoteSenderId.HasValue ? senderMap.GetValueOrDefault(m.QuoteSenderId.Value) : null;
                return new ChatMessageDto
                {
                    Id = m.Id,
                    ConversationId = m.ConversationId,
                    SenderId = m.SenderId,
                    SenderAccount = s?.Account ?? m.SenderId.ToString(),
                    SenderDisplayName = s?.DisplayName,
                    Content = m.Content,
                    MsgType = NormalizeMsgType(m.MsgType, m.Content),
                    QuoteId = m.QuoteId,
                    QuoteText = m.QuoteText,
                    QuoteSenderId = m.QuoteSenderId,
                    QuoteSenderName = qs == null ? null : (qs.DisplayName ?? qs.Account),
                    RefRecordId = m.RefRecordId,
                    Mentions = ParseMentions(m.Mentions),
                    CreateTime = m.CreateTime,
                };
            }).ToList();
        }

        /// <summary>按 Id 查询单条消息：校验成员身份、消息属本会话、且在我方清空水位之后；用于引用点击定位，区分“未加载”与“已不可见”。</summary>
        public ChatMessageDto? GetMessageById(Guid userId, long conversationId, long messageId)
        {
            var member = EnsureMember(userId, conversationId);
            var m = _fsql.Select<ChatMessageEntity>()
                .Where(x => x.Id == messageId && x.ConversationId == conversationId)
                .First();
            if (m == null) return null;
            if (member.ClearBeforeMessageId > 0 && m.Id <= member.ClearBeforeMessageId) return null;

            var senderMap = GetUserInfoMap(new List<Guid> { m.SenderId });
            var s = senderMap.GetValueOrDefault(m.SenderId);
            return new ChatMessageDto
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                SenderId = m.SenderId,
                SenderAccount = s?.Account ?? m.SenderId.ToString(),
                SenderDisplayName = s?.DisplayName,
                Content = m.Content,
                MsgType = NormalizeMsgType(m.MsgType, m.Content),
                QuoteId = m.QuoteId,
                QuoteText = m.QuoteText,
                QuoteSenderId = m.QuoteSenderId,
                QuoteSenderName = null,
                RefRecordId = m.RefRecordId,
                Mentions = ParseMentions(m.Mentions),
                CreateTime = m.CreateTime,
            };
        }

        /// <summary>发送消息：单聊走 peerId；群聊 peerId 为 Empty，由 conversationId 定位会话。双向屏蔽仅对单聊生效；quoteId&gt;0 时引用本会话已有消息（快照固化）。</summary>
        public ChatMessageDto SendMessage(Guid userId, Guid peerId, long conversationId, string content, long quoteId = 0, int msgType = 0, List<string>? mentions = null)
        {
            var text = (content ?? string.Empty).Trim();
            if (text.Length == 0) throw new BadRequestException("消息内容不能为空");
            if (text.Length > MaxMessageLength)
                throw new BadRequestException($"消息长度不能超过 {MaxMessageLength} 字");
            if (msgType is not (0 or 1))
                throw new BadRequestException("不支持的消息类型");
            if (msgType == 1 && !IsChatImagePath(text))
                throw new BadRequestException("图片路径格式无效");

            long convId;
            var now = DateTime.Now;
            ChatConversationEntity conv;

            if (peerId != Guid.Empty)
            {
                // 单聊
                if (peerId == userId) throw new BadRequestException("不能给自己发送消息");
                var peerExists = _fsql.Select<SysUserEntity>()
                    .Where(u => u.Id == peerId && u.Enabled && !u.IsDeleted)
                    .Any();
                if (!peerExists) throw new NotFoundException("对方用户不存在或已停用");

                // 双向屏蔽即拒收：透明提示而非静默投递
                var (blockedByMe, blockedMe) = GetBlockFlags(userId, new List<Guid> { peerId });
                if (blockedByMe.Contains(peerId))
                    throw new BadRequestException("你已屏蔽对方，请先取消屏蔽后再发送消息");
                if (blockedMe.Contains(peerId))
                    throw new BadRequestException("对方暂无法接收你的消息");

                convId = GetOrCreateConversation(userId, peerId);
                conv = _fsql.Select<ChatConversationEntity>().Where(c => c.Id == convId).First()
                    ?? throw new NotFoundException("会话不存在");
            }
            else
            {
                // 群聊
                if (conversationId <= 0) throw new BadRequestException("群聊消息须指定 conversationId");
                var member = EnsureMember(userId, conversationId);
                convId = conversationId;
                conv = _fsql.Select<ChatConversationEntity>().Where(c => c.Id == convId).First()
                    ?? throw new NotFoundException("会话不存在");
                if (conv.ConversationType != 1) throw new BadRequestException("该会话不是群聊");
                // 更新当前成员的最后活跃时间等可在此扩展
            }

            // 引用快照：校验原消息属于本会话且在我方可见范围，固化文本供日后展示
            string? quoteText = null;
            Guid? quoteSenderId = null;
            string? quoteSenderName = null;
            if (quoteId > 0)
            {
                var quoted = _fsql.Select<ChatMessageEntity>()
                    .Where(m => m.Id == quoteId && m.ConversationId == convId)
                    .First();
                if (quoted == null) throw new BadRequestException("引用的消息不存在或不属于该会话");

                var myMember = _fsql.Select<ChatConversationMemberEntity>()
                    .Where(m => m.ConversationId == convId && m.UserId == userId)
                    .First(m => m.ClearBeforeMessageId);
                if (quoted.Id <= myMember) throw new BadRequestException("引用的消息已被你删除");

                quoteSenderId = quoted.SenderId;
                quoteText = TypePreview(quoted.MsgType, quoted.Content);
                var quotedSender = GetUserInfoMap(new List<Guid> { quoted.SenderId }).GetValueOrDefault(quoted.SenderId);
                quoteSenderName = quotedSender == null ? null : (quotedSender.DisplayName ?? quotedSender.Account);
            }

            var preview = TypePreview(msgType, text);
            var mentionJson = mentions is { Count: > 0 } ? JsonSerializer.Serialize(mentions) : null;

            long messageId = 0;
            _fsql.Transaction(() =>
            {
                messageId = _fsql.Insert(new ChatMessageEntity
                {
                    ConversationId = convId,
                    SenderId = userId,
                    MsgType = msgType,
                    Content = text,
                    QuoteId = quoteId,
                    QuoteText = quoteText,
                    QuoteSenderId = quoteSenderId,
                    Mentions = mentionJson,
                    CreateTime = now,
                }).ExecuteIdentity();

                _fsql.Update<ChatConversationEntity>()
                    .Set(c => c.LastMessageId, messageId)
                    .Set(c => c.LastSenderId, userId)
                    .Set(c => c.LastMessageTime, now)
                    .Set(c => c.LastMessageText, preview)
                    .Where(c => c.Id == convId)
                    .ExecuteAffrows();

                // 双方/全体成员取消隐藏：删除过的会话收到新消息自动恢复显示
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
                MsgType = msgType,
                QuoteId = quoteId,
                QuoteText = quoteText,
                QuoteSenderId = quoteSenderId,
                QuoteSenderName = quoteSenderName,
                Mentions = mentions ?? new List<string>(),
                CreateTime = now,
            };
        }

        /// <summary>创建群聊：群名必填，成员至少 1 人（创建者自动加入），上限 1000 人。</summary>
        public ChatConversationDto CreateGroup(Guid creatorId, string name, List<Guid> memberIds)
        {
            var groupName = (name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(groupName)) throw new BadRequestException("群聊名称不能为空");
            if (groupName.Length > 100) throw new BadRequestException("群聊名称不能超过 100 字符");

            var distinctMembers = memberIds?.Where(id => id != Guid.Empty && id != creatorId).Distinct().ToList() ?? new List<Guid>();
            if (distinctMembers.Count == 0) throw new BadRequestException("群聊至少需要一名其他成员");
            if (distinctMembers.Count > 999) throw new BadRequestException("群聊人数不能超过 1000 人");

            // 校验成员存在且启用
            var validUserIds = _fsql.Select<SysUserEntity>()
                .Where(u => distinctMembers.Contains(u.Id) && u.Enabled && !u.IsDeleted)
                .ToList(u => u.Id);
            if (validUserIds.Count != distinctMembers.Count)
                throw new BadRequestException("部分成员不存在或已停用");

            var now = DateTime.Now;
            long convId = 0;
            _fsql.Transaction(() =>
            {
                convId = _fsql.Insert(new ChatConversationEntity
                {
                    UserKey = string.Empty,
                    ConversationType = 1,
                    GroupName = groupName,
                    CreatorId = creatorId,
                    CreateTime = now,
                }).ExecuteIdentity();

                var allMembers = new List<Guid> { creatorId }.Concat(validUserIds).ToList();
                _fsql.Insert(allMembers.Select(uid => new ChatConversationMemberEntity
                {
                    ConversationId = convId,
                    UserId = uid,
                    Role = uid == creatorId ? 1 : 0,
                    CreateTime = now,
                }).ToList()).ExecuteAffrows();
            });

            // 返回会话列表项（前端直接插入列表）
            return new ChatConversationDto
            {
                ConversationId = convId,
                ConversationType = 1,
                PeerId = Guid.Empty,
                PeerAccount = string.Empty,
                PeerDisplayName = groupName,
                PeerAvatar = null,
                GroupName = groupName,
                MemberCount = validUserIds.Count + 1,
                UnreadCount = 0,
                Muted = false,
                BlockedByMe = false,
                BlockedMe = false,
            };
        }

        /// <summary>获取群聊成员列表：仅会话成员可查看。</summary>
        public List<ChatGroupMemberDto> GetGroupMembers(Guid userId, long conversationId)
        {
            EnsureMember(userId, conversationId);
            var conv = _fsql.Select<ChatConversationEntity>().Where(c => c.Id == conversationId).First();
            if (conv == null || conv.ConversationType != 1) throw new NotFoundException("群聊不存在");

            var members = _fsql.Select<ChatConversationMemberEntity>()
                .Where(m => m.ConversationId == conversationId)
                .ToList();
            var userIds = members.Select(m => m.UserId).ToList();
            var userMap = GetUserInfoMap(userIds);

            return members.Select(m =>
            {
                var u = userMap.GetValueOrDefault(m.UserId);
                return new ChatGroupMemberDto
                {
                    UserId = m.UserId,
                    Account = u?.Account ?? m.UserId.ToString(),
                    DisplayName = u?.DisplayName,
                    Avatar = u?.Avatar,
                    Online = false, // Api 层填充
                    Role = m.Role,
                };
            }).ToList();
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

            // 先按会话冗余字段过滤出"可能有未读"的会话（隐藏/最后是我发的/已单方面删除的直接跳过），再逐会话精确统计
            var total = 0;
            foreach (var m in members)
            {
                if (m.Hidden || !convs.TryGetValue(m.ConversationId, out var conv)) continue;
                if (conv.LastSenderId != userId && conv.LastMessageId > m.ReadMessageId && conv.LastMessageId > m.ClearBeforeMessageId)
                    total += (int)_fsql.Select<ChatMessageEntity>()
                        .Where(msg => msg.ConversationId == conv.Id && msg.Id > m.ReadMessageId && msg.Id > m.ClearBeforeMessageId && msg.SenderId != userId)
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

        // ===== 单方面删除 / 转发 =====

        /// <summary>单方面清空我方聊天记录：删除水位推到会话当前最大消息 Id（消息本体保留，对方不受影响）；已读水位同步推进使未读归零。幂等：重复清空无副作用。</summary>
        public void ClearMessagesMySide(Guid userId, long conversationId)
        {
            var member = EnsureMember(userId, conversationId);
            var maxId = _fsql.Select<ChatMessageEntity>()
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Id)
                .First(m => m.Id);
            var watermark = Math.Max(maxId, member.ClearBeforeMessageId);
            if (watermark == member.ClearBeforeMessageId && watermark <= member.ReadMessageId) return; // 无新消息且已清空：幂等返回

            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.ClearBeforeMessageId, watermark)
                .Set(m => m.ReadMessageId, Math.Max(watermark, member.ReadMessageId))
                .Where(m => m.Id == member.Id)
                .ExecuteAffrows();
        }

        /// <summary>逐条转发：源消息逐条复制到目标会话（发送者=我，不带引用），返回新生成的消息列表（供 Api 层推送给双方）。</summary>
        public List<ChatMessageDto> ForwardMessages(Guid userId, List<long> messageIds, Guid targetPeerId)
        {
            var (sourceConvId, ordered) = LoadForwardableMessages(userId, messageIds);
            var targetConvId = EnsureForwardTarget(userId, targetPeerId);
            var now = DateTime.Now;

            var result = new List<ChatMessageDto>();
            _fsql.Transaction(() =>
            {
                long lastId = 0;
                foreach (var src in ordered)
                {
                    // 兼容首版图片误存 MsgType=0：转发时顺带落成正确类型。
                    var srcType = NormalizeMsgType(src.MsgType, src.Content);
                    lastId = _fsql.Insert(new ChatMessageEntity
                    {
                        ConversationId = targetConvId,
                        SenderId = userId,
                        MsgType = srcType,
                        Content = src.Content,
                        CreateTime = now,
                    }).ExecuteIdentity();
                    result.Add(new ChatMessageDto
                    {
                        Id = lastId,
                        ConversationId = targetConvId,
                        SenderId = userId,
                        Content = src.Content,
                        MsgType = srcType,
                        CreateTime = now,
                    });
                }

                var last = ordered[^1];
                TouchConversation(targetConvId, userId, lastId, TypePreview(last.MsgType, last.Content), now);
            });
            return result;
        }

        /// <summary>合并转发：源消息快照固化为 ChatForwardRecord + 目标会话插一条“聊天记录”卡片；返回卡片消息（供 Api 层推送给双方）。</summary>
        public ChatMessageDto ForwardMerged(Guid userId, List<long> messageIds, Guid targetPeerId)
        {
            var (sourceConvId, ordered) = LoadForwardableMessages(userId, messageIds);
            var targetConvId = EnsureForwardTarget(userId, targetPeerId);
            var now = DateTime.Now;

            // 快照：保留原发送者名（删除后仍可看）
            var senderMap = GetUserInfoMap(ordered.Select(m => m.SenderId).Distinct().ToList());
            var items = ordered.Select(m =>
            {
                var s = senderMap.GetValueOrDefault(m.SenderId);
                return new ChatForwardItemDto
                {
                    SenderName = s == null ? m.SenderId.ToString() : (s.DisplayName ?? s.Account),
                    MsgType = NormalizeMsgType(m.MsgType, m.Content),
                    Content = m.Content,
                    Time = m.CreateTime,
                };
            }).ToList();

            // 标题：“我和某某的聊天记录”
            var conv = _fsql.Select<ChatConversationEntity>().Where(c => c.Id == sourceConvId).First();
            var peerId = conv == null ? Guid.Empty : ParsePeerFromKey(conv.UserKey, userId);
            var nameMap = GetUserInfoMap(new List<Guid> { userId, peerId });
            var myInfo = nameMap.GetValueOrDefault(userId);
            var peerInfo = nameMap.GetValueOrDefault(peerId);
            var myName = myInfo == null ? "我" : (myInfo.DisplayName ?? myInfo.Account);
            var peerName = peerInfo == null ? "对方" : (peerInfo.DisplayName ?? peerInfo.Account);
            var title = $"{myName}和{peerName}的聊天记录";

            long messageId = 0;
            long recordId = 0;
            _fsql.Transaction(() =>
            {
                recordId = _fsql.Insert(new ChatForwardRecordEntity
                {
                    CreatorId = userId,
                    Title = title,
                    ContentJson = JsonSerializer.Serialize(items),
                    CreateTime = now,
                }).ExecuteIdentity();

                messageId = _fsql.Insert(new ChatMessageEntity
                {
                    ConversationId = targetConvId,
                    SenderId = userId,
                    MsgType = 2,
                    Content = title,
                    RefRecordId = recordId,
                    CreateTime = now,
                }).ExecuteIdentity();

                TouchConversation(targetConvId, userId, messageId, TypePreview(2, string.Empty), now);
            });

            return new ChatMessageDto
            {
                Id = messageId,
                ConversationId = targetConvId,
                SenderId = userId,
                Content = title,
                MsgType = 2,
                RefRecordId = recordId,
                CreateTime = now,
            };
        }

        /// <summary>查看合并转发记录（快照只读）：创建者，或收到过该卡片的人可看。</summary>
        public ChatForwardRecordDto GetForwardRecord(Guid userId, long recordId)
        {
            var record = _fsql.Select<ChatForwardRecordEntity>().Where(r => r.Id == recordId).First();
            if (record == null) throw new NotFoundException("聊天记录不存在");

            if (record.CreatorId != userId)
            {
                // 非创建者：必须是收到过该卡片消息的会话成员（未收到过卡片的人无法凭 Id 偷看）
                var cardConvIds = _fsql.Select<ChatMessageEntity>()
                    .Where(m => m.RefRecordId == recordId)
                    .ToList(m => m.ConversationId)
                    .Distinct().ToList();
                var allowed = cardConvIds.Count > 0 && _fsql.Select<ChatConversationMemberEntity>()
                    .Where(mm => cardConvIds.Contains(mm.ConversationId) && mm.UserId == userId)
                    .Any();
                if (!allowed) throw new NotFoundException("聊天记录不存在");
            }

            var items = (JsonSerializer.Deserialize<List<ChatForwardItemDto>>(record.ContentJson) ?? new List<ChatForwardItemDto>())
                .Where(i => i != null).ToList();
            return new ChatForwardRecordDto { Id = record.Id, Title = record.Title, Items = items };
        }

        // ===== 转发辅助 =====

        /// <summary>加载特转发的源消息：须非空、上限 50、属同一会话、我是该会话成员且消息在我方可见范围（未被单方面删除）。</summary>
        private (long SourceConvId, List<ChatMessageEntity> Ordered) LoadForwardableMessages(Guid userId, List<long> messageIds)
        {
            if (messageIds == null || messageIds.Count == 0) throw new BadRequestException("未选择要转发的消息");
            if (messageIds.Count > 50) throw new BadRequestException("单次最多转发 50 条消息");

            var msgs = _fsql.Select<ChatMessageEntity>().Where(m => messageIds.Contains(m.Id)).ToList();
            if (msgs.Count != messageIds.Distinct().Count()) throw new BadRequestException("部分消息不存在");

            var sourceConvId = msgs[0].ConversationId;
            if (msgs.Any(m => m.ConversationId != sourceConvId)) throw new BadRequestException("只能转发同一会话内的消息");

            var member = EnsureMember(userId, sourceConvId);
            if (msgs.Any(m => m.Id <= member.ClearBeforeMessageId)) throw new BadRequestException("部分消息已被你删除，无法转发");

            return (sourceConvId, msgs.OrderBy(m => m.Id).ToList());
        }

        /// <summary>转发目标校验：不能是自己/不存在/停用，双向屏蔽拒收（与 SendMessage 一致）；返回目标会话 Id。</summary>
        private long EnsureForwardTarget(Guid userId, Guid targetPeerId)
        {
            if (targetPeerId == userId) throw new BadRequestException("不能转发给自己");
            var peerExists = _fsql.Select<SysUserEntity>()
                .Where(u => u.Id == targetPeerId && u.Enabled && !u.IsDeleted)
                .Any();
            if (!peerExists) throw new NotFoundException("目标用户不存在或已停用");

            var (blockedByMe, blockedMe) = GetBlockFlags(userId, new List<Guid> { targetPeerId });
            if (blockedByMe.Contains(targetPeerId))
                throw new BadRequestException("你已屏蔽对方，请先取消屏蔽后再转发");
            if (blockedMe.Contains(targetPeerId))
                throw new BadRequestException("对方暂无法接收你的消息");

            return GetOrCreateConversation(userId, targetPeerId);
        }

        /// <summary>转发后更新会话最后消息冗余 + 双方取消隐藏（转发到被删除的会话同样恢复显示）。</summary>
        private void TouchConversation(long conversationId, Guid senderId, long lastMessageId, string preview, DateTime now)
        {
            _fsql.Update<ChatConversationEntity>()
                .Set(c => c.LastMessageId, lastMessageId)
                .Set(c => c.LastSenderId, senderId)
                .Set(c => c.LastMessageTime, now)
                .Set(c => c.LastMessageText, preview)
                .Where(c => c.Id == conversationId)
                .ExecuteAffrows();

            _fsql.Update<ChatConversationMemberEntity>()
                .Set(m => m.Hidden, false)
                .Where(m => m.ConversationId == conversationId)
                .ExecuteAffrows();
        }

        /// <summary>本服务生成的聊天图片相对路径格式；用于兼容首版误以文本类型保存的图片。</summary>
        private static bool IsChatImagePath(string? content)
            => !string.IsNullOrEmpty(content) && System.Text.RegularExpressions.Regex.IsMatch(
                content, @"^\d{6}/[0-9a-f]{32}\.(jpg|jpeg|png|gif|webp|bmp)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>把首版误存为文本、但路径格式明确的图片规范为图片类型。</summary>
        private static int NormalizeMsgType(int msgType, string content)
            => msgType == 0 && IsChatImagePath(content) ? 1 : msgType;

        /// <summary>解析 Mentions JSON 字符串为 Guid 字符串列表。</summary>
        private static List<string> ParseMentions(string? mentionsJson)
        {
            if (string.IsNullOrWhiteSpace(mentionsJson)) return new List<string>();
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(mentionsJson);
                return list?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        /// <summary>按消息类型生成预览文案：图片→[图片]，卡片→[聊天记录]，文本截断到预览长度。</summary>
        private static string TypePreview(int msgType, string content)
            => NormalizeMsgType(msgType, content) switch
            {
                1 => "[图片]",
                2 => "[聊天记录]",
                _ => content.Length > PreviewLength ? content[..PreviewLength] : content,
            };
    }
}
