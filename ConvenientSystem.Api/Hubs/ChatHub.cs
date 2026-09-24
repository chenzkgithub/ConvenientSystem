using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Hubs
{
    /// <summary>
    /// 聊天实时中心：连接即用户（GUID 定位，由 SignalRUserIdProvider 从 JWT claim 提供）。
    /// 本 Hub 不定义客户端可调用的发送方法——发送走 REST（ChatController），推送统一由服务端
    /// 通过 IHubContext&lt;ChatHub&gt;.Clients.User(guid) 定向完成，与业务 Service 层解耦。
    /// 客户端事件约定：
    /// - ReceiveMessage(ChatMessageDto)：新消息（发给会话双方）；
    /// - ReadAck(conversationId, readerId, messageId)：已读水位推进回执（发给会话对方）；
    /// - UserOnline / UserOffline(userId)：上下线状态广播（发给全部连接，前端维护在线集合）；
    /// - FriendRequest(FriendRequestDto)：新好友申请（发给接收方，前端拉取申请列表/角标）；
    /// - FriendRequestHandled(FriendRequestHandledDto)：好友申请处理结果（发给申请人，前端提示并刷新）；
    /// - NoticeCreated()：新系统通知广播（发给全部连接，无参数；前端收到后各自拉取列表/未读数，
    ///   定向通知的可见性过滤由拉取接口完成，不在推送侧复制）。通知与聊天共用本连接（单连接多事件）。
    /// </summary>
    [Authorize]
    public class ChatHub : Hub
    {
        /// <summary>在线连接注册表：userId → 该用户的全部连接 Id（同一账号多标签页共享同一 userId）。
        /// 静态成员跨 Hub 实例共享（Hub 为每连接一个实例）。</summary>
        private static readonly ConcurrentDictionary<Guid, HashSet<string>> _connections = new();

        /// <summary>当前连接的用户 Id。</summary>
        private Guid CurrentUserId => Guid.TryParse(Context.UserIdentifier, out var id) ? id : Guid.Empty;

        public override async Task OnConnectedAsync()
        {
            var userId = CurrentUserId;
            if (userId == Guid.Empty) return;

            // 上线广播：内部系统规模小（≤100 在线），直接全量广播在线状态；
            // 重连时（断开后立即重连）也会广播一次 UserOnline，前端 Set 去重天然幂等
            var isNewUser = _connections.TryAdd(userId, new HashSet<string> { Context.ConnectionId });
            if (!isNewUser)
            {
                var set = _connections.GetOrAdd(userId, _ => new HashSet<string>());
                lock (set) set.Add(Context.ConnectionId);
            }
            await Clients.All.SendAsync("UserOnline", userId.ToString());
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = CurrentUserId;
            if (userId == Guid.Empty) return;

            if (_connections.TryGetValue(userId, out var remaining))
            {
                bool lastConnection;
                lock (remaining)
                {
                    remaining.Remove(Context.ConnectionId);
                    lastConnection = remaining.Count == 0;
                }
                if (lastConnection)
                {
                    // 防泄漏：空集合移除键（TryRemove 键值对重载保证与并发新增不冲突）
                    _connections.TryRemove(KeyValuePair.Create(userId, remaining));
                    // 该用户最后一个连接断开才广播下线（多标签页其余连接仍在线）
                    await Clients.All.SendAsync("UserOffline", userId.ToString());
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}
