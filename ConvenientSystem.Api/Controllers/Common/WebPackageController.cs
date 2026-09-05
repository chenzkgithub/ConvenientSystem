using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// Web 前端版本包控制器：管理员上传/激活/删除，桌面端查询/下载。
    /// GetActive 和 Download 允许匿名访问（桌面端启动时未登录）。
    /// </summary>
    [Area("Common")]
    public class WebPackageController : BaseController
    {
        private readonly IWebPackageService _service;

        public WebPackageController(IWebPackageService service)
        {
            _service = service;
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

        /// <summary>上传版本包 zip（自动激活为新版本）。</summary>
        [HttpPost]
        [PermissionAuthorize("web-package:upload")]
        [RequestSizeLimit(210_000_000)] // 200MB + 余量
        public ActionResult<Shared.Model.Common.WebPackageDto> Upload(
            [FromForm] string version,
            IFormFile file,
            [FromForm] string? description)
            => Ok(_service.Upload(version, file, description, CurrentUserId));

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
