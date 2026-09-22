using ConvenientSystem.Service.Ai;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Hubs
{
    /// <summary>
    /// AI 流式生成 SignalR 推送器：后台生成循环把增量/终态以 AiResponseChunk 事件定向推给发起人。
    /// 复用 ChatHub 连接（单连接多事件，前端 chat store 统一监听后经 window 事件桥接给 ai store）；
    /// 节流由 AiChatService 后台循环控制（300ms 间隔 + 终态必推）。发送为 fire-and-forget：
    /// 推送失败不影响生成（内容已落库），前端轮询 Ai/Messages 兜底。
    /// </summary>
    public sealed class SignalRAiChunkNotifier : IAiChunkNotifier
    {
        private readonly IHubContext<ChatHub> _hub;

        public SignalRAiChunkNotifier(IHubContext<ChatHub> hub)
        {
            _hub = hub;
        }

        public Task NotifyUserAsync(Guid userId, AiChunkDto chunk)
            => _hub.Clients.User(userId.ToString()).SendAsync("AiResponseChunk", chunk);
    }
}
