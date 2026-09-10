using System.IO.Compression;
using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// Web 前端版本包控制器：管理员上传/激活/删除，桌面端查询/下载。
    /// GetActive 和 Download 允许匿名访问（桌面端启动时未登录）。
/// 上传新版本后创建系统通知（含下载链接）并广播 NoticeCreated，全员秒级收到。
    /// </summary>
    [Area("Common")]
    public class WebPackageController : BaseController
    {
        private readonly IWebPackageService _service;
        private readonly INoticeService _noticeService;
        private readonly IHubContext<ChatHub> _hubContext;

        public WebPackageController(IWebPackageService service, INoticeService noticeService, IHubContext<ChatHub> hubContext)
        {
            _service = service;
            _noticeService = noticeService;
            _hubContext = hubContext;
        }

        /// <summary>获取当前激活的版本包信息（桌面端启动时检查更新）。</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<object> GetActive()
        {
            var pkg = _service.GetActive();
            if (pkg == null) return Ok(new { hasVersion = false });
            return Ok(new
            {
                hasVersion = true,
                id = pkg.Id,
                version = pkg.Version,
                fileSize = pkg.FileSize,
                description = pkg.Description,
                createTime = pkg.CreateTime,
            });
        }

        /// <summary>下载当前激活的版本包 zip 文件（桌面端调用）。</summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Download()
        {
            try
            {
                var (filePath, fileName) = _service.GetActiveFilePath();
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "application/zip", fileName);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>获取全部版本包列表（管理页面）。</summary>
        [HttpGet]
        [PermissionAuthorize("web-package")]
        public ActionResult<List<Shared.Model.Common.WebPackageDto>> GetList()
            => Ok(_service.GetList());

        /// <summary>
        /// 上传版本包 zip（自动激活为新版本）。上传成功后创建系统通知（level=2，含下载链接）
/// 并广播 NoticeCreated：通知正文的相对链接在浏览器/桌面壳内各自解析到同域代理，点击直接下载。
        /// </summary>
        [HttpPost]
        [PermissionAuthorize("web-package:upload")]
        [RequestSizeLimit(210_000_000)] // 200MB + 余量
        public async Task<ActionResult<Shared.Model.Common.WebPackageDto>> Upload(
            [FromForm] string version,
            IFormFile file,
            [FromForm] string? description)
        {
            // 上传前校验 zip 结构：挡住误传的非前端 zip（如桌面安装包产物），
            // 那种包一旦激活，所有客户端热更新都会解压失败
            var zipError = ValidateWebPackageZip(file);
            if (zipError != null) return BadRequest(new { message = zipError });

            var dto = _service.Upload(version, file, description, CurrentUserId);

            var content = $"[Web前端] v{dto.Version}（{FormatSize(dto.FileSize)}）"
                + (string.IsNullOrWhiteSpace(description) ? "" : $"\n{description.Trim()}")
                + "\n下载：/api/Common/WebPackage/Download";
            _noticeService.CreateSystemNotice($"Web 前端已发布 v{dto.Version}", content, level: 2);
            await _hubContext.Clients.All.SendAsync("NoticeCreated");
            return Ok(dto);
        }

        /// <summary>文件大小人类可读化（B/KB/MB/GB，保留一位小数）。</summary>
        private static string FormatSize(long bytes)
            => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:F1} GB"
            : bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:F1} MB"
            : bytes >= 1024 ? $"{bytes / 1024.0:F0} KB"
            : $"{bytes} B";

        /// <summary>
        /// 校验 Web 包 zip 结构（与桌面端 NormalizeExtractedRoot 解包规则一致）：
        /// index.html 位于 zip 根级，或恰有一个含 index.html 的一级子目录。
        /// 通过返回 null，不通过返回给用户的错误消息。
        /// </summary>
        private static string? ValidateWebPackageZip(IFormFile file)
        {
            try
            {
                using var zip = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
                var names = zip.Entries.Select(e => e.FullName).ToList();

                // 标准结构：index.html 位于 zip 根级
                if (names.Contains("index.html")) return null;

                // 兼容「压缩了构建输出文件夹本身」：恰有一个一级子目录含 index.html
                var rootsWithIndex = names
                    .Where(n => n.EndsWith("/index.html", StringComparison.Ordinal))
                    .Select(n => n[..n.LastIndexOf('/')])
                    .Where(root => !root.Contains('/'))
                    .Distinct()
                    .ToList();
                if (rootsWithIndex.Count == 1) return null;

                return "包结构异常：zip 根目录未找到 index.html，请上传 Web 前端构建产物"
                    + "（npm run build 后将 dist 目录内容打包为 zip）；"
                    + "当前文件疑似不是前端包（例如桌面安装包产物）";
            }
            catch (InvalidDataException)
            {
                return "文件不是有效的 zip 包，请重新打包上传";
            }
        }

        /// <summary>激活指定版本。</summary>
        [HttpPost]
        [PermissionAuthorize("web-package:activate")]
        public ActionResult Activate([FromBody] Shared.Model.Common.WebPackageActivateDto dto)
        {
            _service.Activate(dto.Id);
            return Ok();
        }

        /// <summary>停用指定版本（取消激活状态）。</summary>
        [HttpPost]
        [PermissionAuthorize("web-package:activate")]
        public ActionResult Deactivate([FromBody] Shared.Model.Common.WebPackageActivateDto dto)
        {
            _service.Deactivate(dto.Id);
            return Ok();
        }

        /// <summary>删除版本包（不允许删除激活版本）。</summary>
        [HttpDelete]
        [PermissionAuthorize("web-package:delete")]
        public ActionResult Delete([FromQuery] int id)
        {
            _service.Delete(id);
            return Ok();
        }

        /// <summary>修改版本号和更新说明。</summary>
        [HttpPost]
        [PermissionAuthorize("web-package:edit")]
        public ActionResult Update([FromBody] Shared.Model.Common.WebPackageUpdateDto dto)
        {
            _service.Update(dto.Id, dto.Version, dto.Description);
            return Ok();
        }
    }
}
