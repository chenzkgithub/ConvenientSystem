using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Hubs
{
    /// <summary>
    /// 异步任务进度 SignalR 推送器：任务进度变化时向发起人定向推 AsyncTaskProgress 事件。
    /// 复用 ChatHub 连接（单连接多事件，前端 chat store 统一监听）；推送频次由 AsyncTaskCenter 节流
    /// （运行中 800ms 一条，终态必推）。发送为 fire-and-forget：推送失败不影响任务执行，
    /// 前端轮询 AsyncTaskController 兜底。
    /// </summary>
    public sealed class SignalRAsyncTaskNotifier : IAsyncTaskNotifier
    {
        private readonly IHubContext<ChatHub> _hub;

        public SignalRAsyncTaskNotifier(IHubContext<ChatHub> hub)
        {
            _hub = hub;
        }

        public void Notify(Guid userId, AsyncTaskDto snapshot)
            => _ = _hub.Clients.User(userId.ToString()).SendAsync("AsyncTaskProgress", snapshot);
    }
}
