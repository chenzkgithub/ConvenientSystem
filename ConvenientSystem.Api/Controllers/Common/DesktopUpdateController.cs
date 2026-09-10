using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 桌面程序自更新控制器：客户端检查/下载，管理端上传/激活/删除。
    /// Check/Download 允许匿名访问（桌面端启动时可能未登录）。
/// 上传新版本后创建系统通知（含下载链接）并广播 NoticeCreated，全员秒级收到。
    /// </summary>
    [Area("Common")]
    public class DesktopUpdateController : BaseController
    {
        private readonly IDesktopUpdateService _service;
        private readonly INoticeService _noticeService;
        private readonly IHubContext<ChatHub> _hubContext;

        public DesktopUpdateController(IDesktopUpdateService service, INoticeService noticeService, IHubContext<ChatHub> hubContext)
        {
            _service = service;
            _noticeService = noticeService;
            _hubContext = hubContext;
        }

        /// <summary>桌面端启动时检查是否有新版本。</summary>
        [AllowAnonymous]
        [HttpGet]
        public ActionResult<DesktopUpdateCheckResult> Check([FromQuery] string version)
        {
            return Ok(_service.Check(version));
        }

        /// <summary>下载当前激活的桌面安装包。</summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Download()
        {
            var (filePath, fileName) = _service.GetActiveFilePath();
            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, "application/octet-stream", fileName);
        }

        /// <summary>管理端：获取全部桌面安装包列表。</summary>
        [HttpGet]
        [PermissionAuthorize("desktop-package")]
        public ActionResult<List<DesktopPackageDto>> List()
        {
            return Ok(_service.GetList());
        }

        /// <summary>
        /// 管理端：上传桌面安装包 exe（自动激活为新版本）。上传成功后创建系统通知（level=2，含下载链接）
/// 并广播 NoticeCreated：通知正文的相对链接在浏览器/桌面壳内各自解析到同域代理，点击直接下载。
        /// </summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:upload")]
        [RequestSizeLimit(510_000_000)] // 500MB + 余量
        public async Task<ActionResult<DesktopPackageDto>> Upload(
            [FromForm] string version,
            IFormFile file,
            [FromForm] string? description)
        {
            var dto = _service.Upload(version, file, description, CurrentUserId);

            var content = $"[桌面端] v{dto.Version}（{FormatSize(dto.FileSize)}）"
                + (string.IsNullOrWhiteSpace(description) ? "" : $"\n{description.Trim()}")
                + "\n下载：/api/Common/DesktopUpdate/Download";
            _noticeService.CreateSystemNotice($"桌面安装包已发布 v{dto.Version}", content, level: 2);
            await _hubContext.Clients.All.SendAsync("NoticeCreated");
            return Ok(dto);
        }

        /// <summary>文件大小人类可读化（B/KB/MB/GB，保留一位小数）。</summary>
        private static string FormatSize(long bytes)
            => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:F1} GB"
            : bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:F1} MB"
            : bytes >= 1024 ? $"{bytes / 1024.0:F0} KB"
            : $"{bytes} B";

        /// <summary>管理端：激活指定版本。</summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:activate")]
        public ActionResult Activate([FromBody] WebPackageActivateDto dto)
        {
            _service.Activate(dto.Id);
            return Ok();
        }

        /// <summary>管理端：停用指定版本。</summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:activate")]
        public ActionResult Deactivate([FromBody] WebPackageActivateDto dto)
        {
            _service.Deactivate(dto.Id);
            return Ok();
        }

        /// <summary>管理端：删除指定版本。</summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:delete")]
        public ActionResult Delete([FromBody] WebPackageActivateDto dto)
        {
            _service.Delete(dto.Id);
            return Ok();
        }

        /// <summary>管理端：更新指定版本的元数据。</summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:edit")]
        public ActionResult Update([FromBody] WebPackageDto dto)
        {
            _service.Update(dto.Id, dto.Version, dto.Description);
            return Ok();
        }
    }
}
