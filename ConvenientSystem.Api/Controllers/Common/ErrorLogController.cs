using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统错误日志接口：查看全局异常过滤器捕获的未处理异常记录。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("error-log")]
    public class ErrorLogController : BaseController
    {
        private readonly IErrorLogService _errorLogService;

        public ErrorLogController(IErrorLogService errorLogService)
        {
            _errorLogService = errorLogService;
        }

        /// <summary>分页查询错误日志</summary>
        [HttpGet]
        public ActionResult<PagedResult<ErrorLogDto>> List(
            [FromQuery] string? keyword,
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortOrder = null)
            => Ok(_errorLogService.GetList(keyword, startTime, endTime, page, size, sortField, sortOrder));

        /// <summary>清空全部错误日志</summary>
        [HttpDelete]
        [PermissionAuthorize("error-log:clear")]
        public ActionResult<int> Clear()
            => Ok(_errorLogService.Clear());
    }
}
