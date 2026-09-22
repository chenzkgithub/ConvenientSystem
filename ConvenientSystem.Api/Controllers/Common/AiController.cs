using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Ai;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// AI 对话接口：全局助手抽屉的会话/消息/发送/停止。
    /// 对话为个人数据，但能力准入由 ai-chat 视图权限点控制（管理员未授权的用户不可用）；
    /// 发送为「同步落库 + 后台流式生成」，响应即刻返回消息 Id，内容经 SignalR AiResponseChunk 推送，
    /// SignalR 不可用时前端轮询 Messages（assistant 消息 Status≠0 即完成）兜底。
    /// </summary>
    [Area("Common")]
    [Authorize]
    [PermissionAuthorize("ai-chat")]
    public class AiController : BaseController
    {
        private readonly IAiChatService _chatService;

        public AiController(IAiChatService chatService)
        {
            _chatService = chatService;
        }

        /// <summary>助手抽屉头部状态：开关/配额/模型就绪（一次拉齐，减少请求数）</summary>
        [HttpGet]
        public IActionResult MyStatus() => Ok(_chatService.GetMyStatus(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录")));


        /// <summary>我的会话列表</summary>
        [HttpGet]
        public IActionResult Conversations() => Ok(_chatService.GetConversations(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录")));

        /// <summary>某会话的消息（轮询兜底通道）</summary>
        [HttpGet]
        public IActionResult Messages([FromQuery] long conversationId)
            => Ok(_chatService.GetMessages(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), conversationId));

        /// <summary>删除会话</summary>
        [HttpDelete]
        public IActionResult DeleteConversation([FromQuery] long conversationId)
        {
            _chatService.DeleteConversation(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), conversationId);
            return Ok(new { message = "已删除" });
        }

        /// <summary>发送消息：同步校验配额/落库后返回（AssistantMessageId 对应消息后台流式生成）</summary>
        [HttpPost]
        public IActionResult Send([FromBody] AiSendRequest request) => Ok(_chatService.Send(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), request));

        /// <summary>重新生成：以该 AI 回复之前的上下文重新生成（配额同 Send）</summary>
        [HttpPost]
        public IActionResult Regenerate([FromQuery] long messageId)
            => Ok(_chatService.Regenerate(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), messageId));

        /// <summary>停止生成（前端「停止」按钮）</summary>
        [HttpPost]
        public IActionResult Stop([FromQuery] long messageId)
        {
            _chatService.StopGeneration(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), messageId);
            return Ok(new { message = "已停止" });
        }

        /// <summary>我的 AI 任务列表（create_task 工具创建的待办；助手抽屉任务面板数据源）</summary>
        [HttpGet]
        public IActionResult Tasks()
            => Ok(_chatService.GetTasks(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录")));

        /// <summary>更新任务状态（0 待办 / 1 完成，任务面板勾选）</summary>
        [HttpPost]
        public IActionResult TaskStatus([FromQuery] long taskId, [FromQuery] int status)
        {
            _chatService.UpdateTaskStatus(CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录"), taskId, status);
            return Ok(new { message = "已更新" });
        }
    }
}
