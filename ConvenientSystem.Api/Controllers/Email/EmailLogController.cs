using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Email;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Email;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Email
{
    /// <summary>
    /// 邮件发送日志接口（日志查询 + 任务下拉 + 趋势统计）。
    /// 任务管理已移除，但历史任务与日志仍可查询。
    /// </summary>
    [Area("Email")]
    [PermissionAuthorize("email-log")]
    public class EmailLogController : BaseController
    {
        private readonly IEmailTaskService _taskService;

        public EmailLogController(IEmailTaskService taskService)
        {
            _taskService = taskService;
        }

        /// <summary>查询全部邮件任务（供日志筛选下拉）</summary>
        [HttpGet]
        public ActionResult<List<EmailTaskDto>> Tasks()
            => Ok(_taskService.GetList());

        /// <summary>分页查询发送日志</summary>
        [HttpGet]
        public ActionResult<PagedResult<EmailLogDto>> Logs(int? taskId, int page = 1, int size = 20)
            => Ok(_taskService.GetLogs(taskId, page, size));

        /// <summary>按日发送趋势（days：往前天数，含今天）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> Trend(int days = 7)
            => Ok(_taskService.GetTrend(days));
    }
}
