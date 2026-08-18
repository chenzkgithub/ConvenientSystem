using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// Hangfire 定时任务管理接口
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("hangfire-jobs")]
    public class HangfireJobController : BaseController
    {
        private readonly IHangfireService _hangfireService;

        public HangfireJobController(IHangfireService hangfireService)
        {
            _hangfireService = hangfireService;
        }

        /// <summary>获取周期任务列表</summary>
        [HttpGet]
        public IActionResult GetRecurringJobs() => Ok(_hangfireService.GetRecurringJobs());

        /// <summary>手动触发周期任务</summary>
        [HttpPost]
        [PermissionAuthorize("hangfire-jobs:trigger")]
        public IActionResult TriggerJob([FromBody] HangfireJobRequest request)
        {
            _hangfireService.TriggerJob(request.JobId);
            return Ok(new { message = "已触发" });
        }

        /// <summary>查询周期任务的执行历史（最近 50 次）</summary>
        [HttpGet]
        public IActionResult GetExecutionHistory([FromQuery] string recurringJobId)
            => Ok(_hangfireService.GetExecutionHistory(recurringJobId));
    }

    public class HangfireJobRequest
    {
        public string JobId { get; set; } = "";
    }
}
