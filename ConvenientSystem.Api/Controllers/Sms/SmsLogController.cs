using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Sms;
using ConvenientSystem.Service.Sms;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Sms
{
    /// <summary>
    /// 短信发送日志接口
    /// </summary>
    [Area("Sms")]
    [PermissionAuthorize("sms-log")]
    public class SmsLogController : BaseController
    {
        private readonly ISmsLogService _logService;

        public SmsLogController(ISmsLogService logService)
        {
            _logService = logService;
        }

        /// <summary>查询日志列表</summary>
        [HttpGet]
        public ActionResult<PagedResult<SmsLogDto>> List(
            [FromQuery] int? taskId,
            [FromQuery] string? phone,
            [FromQuery] byte? status,
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortOrder = null)
            => Ok(_logService.GetList(taskId, phone, status, startTime, endTime, page, size, sortField, sortOrder));

        /// <summary>获取统计信息</summary>
        [HttpGet]
        public ActionResult<SmsStatisticsDto> Statistics()
            => Ok(_logService.GetStatistics());

        /// <summary>按日发送趋势（默认近 7 天）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> Trend([FromQuery] int days = 7)
            => Ok(_logService.GetTrend(days));

        /// <summary>获取配额使用情况</summary>
        [HttpGet]
        public ActionResult<SmsQuotaDto> Quota()
            => Ok(_logService.GetQuota());
    }
}
