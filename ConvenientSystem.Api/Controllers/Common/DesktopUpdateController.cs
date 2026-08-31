using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 桌面程序自更新控制器：客户端检查/下载，管理端上传/激活/删除。
    /// Check/Download 允许匿名访问（桌面端启动时可能未登录）。
    /// </summary>
    [Area("Common")]
    public class DesktopUpdateController : BaseController
    {
        private readonly IDesktopUpdateService _service;

        public DesktopUpdateController(IDesktopUpdateService service)
        {
            _service = service;
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

        /// <summary>管理端：上传桌面安装包 exe（自动激活为新版本）。</summary>
        [HttpPost]
        [PermissionAuthorize("desktop-package:upload")]
        [RequestSizeLimit(510_000_000)] // 500MB + 余量
        public ActionResult<DesktopPackageDto> Upload(
            [FromForm] string version,
            IFormFile file,
            [FromForm] string? description)
        {
            return Ok(_service.Upload(version, file, description, CurrentUserId));
        }

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
        [PermissionAuthorize("desktop-package:upload")]
        public ActionResult Update([FromBody] WebPackageDto dto)
        {
            _service.Update(dto.Id, dto.Version, dto.Description);
            return Ok();
        }
    }
}
