using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// Apifox 导入/批量删除与当前用户 Access Token 维护接口。
    /// Access Token 按当前登录用户加密保存到 UserConfig；项目及导入选项只在本次请求中使用。
    /// 导入与删除均为后台任务：启动接口立即返回任务初始快照，进度经 AsyncTaskProgress 推送（AsyncTaskController 轮询兜底）。
    /// </summary>
    [Area("Common")]
    public class ApifoxConfigController : BaseController
    {
        private readonly IApifoxImportService _service;

        public ApifoxConfigController(IApifoxImportService service)
        {
            _service = service;
        }

        /// <summary>读取当前用户的 Apifox Access Token 保存状态，不返回令牌内容。</summary>
        [HttpGet]
        public ActionResult<ApifoxAccessTokenStatusDto> GetMyAccessTokenStatus()
            => Ok(_service.GetMyAccessTokenStatus());

        /// <summary>保存或清除当前用户的 Apifox Access Token，令牌会在服务端保护后写入 UserConfig。</summary>
        [HttpPut]
        public IActionResult SaveMyAccessToken([FromBody] ApifoxAccessTokenSaveRequest request)
        {
            _service.SaveMyAccessToken(request ?? new ApifoxAccessTokenSaveRequest());
            return Ok(new { message = "Apifox Access Token 已保存" });
        }

        /// <summary>启动 OpenAPI 3 JSON 分批导入任务（按 tag 拆批后台执行），返回任务初始快照（含 TaskId）供轮询进度。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public ActionResult<AsyncTaskDto> StartImportOpenApi([FromBody] ApifoxImportRequest request)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            return Ok(_service.StartImport(request ?? new ApifoxImportRequest(), userId));
        }

        /// <summary>启动批量删除任务：删除项目全部接口，或指定目录（含子目录）下的接口；删除后顺带清理空目录。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public ActionResult<AsyncTaskDto> DeleteEndpoints([FromBody] ApifoxDeleteRequest request)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            return Ok(_service.StartDelete(request ?? new ApifoxDeleteRequest(), userId));
        }

        /// <summary>强制终止当前用户指定类型的进行中任务（上次请求超时但实际仍在后台执行时解锁）；返回 { cancelled: 是否终止了任务 }。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public IActionResult CancelRunningTask([FromBody] ApifoxCancelRunningRequest request)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            return Ok(new { cancelled = _service.CancelRunning(request?.Kind, userId) });
        }

        /// <summary>轮询 Apifox 后台任务进度（导入/删除，轻量快照不含 Result）：桌面代理模式下 Apifox 任务在云端执行，前端按任务类型路由到本接口（经代理转发）；任务不存在、已过期（完成超 30 分钟）或非本人任务返回 404。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public IActionResult GetTaskProgress([FromQuery] Guid taskId)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            var progress = _service.GetTaskProgress(taskId, userId);
            return progress is null
                ? NotFound(new { message = "任务不存在或已过期" })
                : Ok(progress);
        }

        /// <summary>读取已完成 Apifox 任务的完整 Result（导入聚合结果/删除统计；轮询快照不携带，页面终态后按需拉取）；
        /// 任务不存在、未完成、已过期或非本人任务返回 404。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public IActionResult GetTaskResult([FromQuery] Guid taskId)
        {
            var userId = CurrentUserId ?? throw new UnauthorizedAccessException("登录状态失效，请重新登录");
            var task = _service.GetTaskResult(taskId, userId);
            return task is null
                ? NotFound(new { message = "任务不存在、未完成或已过期" })
                : Ok(task);
        }
    }
}
