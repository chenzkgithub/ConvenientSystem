using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 实时日志查看器接口
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("system-dashboard")]
    public class LogViewerController : BaseController
    {
        private readonly MemoryLogBuffer _logBuffer;

        public LogViewerController(MemoryLogBuffer logBuffer)
        {
            _logBuffer = logBuffer;
        }

        /// <summary>获取最近日志</summary>
        [HttpGet]
        public IActionResult GetLogs([FromQuery] int count = 100, [FromQuery] string? keyword = null, [FromQuery] string? level = null)
            => Ok(_logBuffer.GetRecent(count, keyword, level));

        /// <summary>清空日志缓冲</summary>
        [HttpPost]
        public IActionResult ClearLogs()
        {
            _logBuffer.Clear();
            return Ok(new { message = "已清空" });
        }
    }
}
