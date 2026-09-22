using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 统一异步任务查询接口：任务由 AsyncTaskCenter 登记（ApiSpec 扫描/生成、Apifox 导入/删除等）。
    /// 任务进度为个人数据，仅登录校验不挂菜单权限码（同 ChatController）。
    /// 进度变化经 SignalR AsyncTaskProgress 事件实时推送到发起人；本接口供前端轮询兜底与刷新恢复。
    /// </summary>
    [Area("Common")]
    [Authorize]
    public class AsyncTaskController : BaseController
    {
        private readonly AsyncTaskCenter _center;

        public AsyncTaskController(AsyncTaskCenter center)
        {
            _center = center;
        }

        /// <summary>查询单个任务进度（轻量快照，不含 Result）；任务不存在、已过期（完成超 30 分钟）或非本人任务返回 404。</summary>
        [HttpGet]
        public IActionResult Get([FromQuery] Guid taskId)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            var task = _center.GetTask(taskId, userId);
            return task is null ? NotFound(new { message = "任务不存在或已过期" }) : Ok(task);
        }

        /// <summary>读取已完成任务的完整 Result（生成任务可达数 MB，轮询/推送不携带，页面终态后按需拉取）；
        /// 任务不存在、未完成、已过期或非本人任务返回 404。</summary>
        [HttpGet]
        public IActionResult GetResult([FromQuery] Guid taskId)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            var task = _center.GetResult(taskId, userId);
            return task is null ? NotFound(new { message = "任务不存在、未完成或已过期" }) : Ok(task);
        }

        /// <summary>当前用户全部未清理任务（运行中 + 完成未超 30 分钟），页面刷新后恢复任务面板用。</summary>
        [HttpGet]
        public IActionResult GetAll()
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            return Ok(_center.GetTasks(userId));
        }
    }
}
