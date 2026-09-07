using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// API 文档生成器：扫描 C# 项目的 Controller 源码，生成 OpenAPI / Postman / Markdown
    /// 等格式的 API 数据文件，供 Apifox / Postman 等工具导入。无状态纯解析，不落库。
    /// </summary>
    [Area("Common")]
    public class ApiSpecController : BaseController
    {
        private readonly IApiSpecService _service;

        public ApiSpecController(IApiSpecService service)
        {
            _service = service;
        }

        /// <summary>支持的导出格式列表（格式卡片网格数据源）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public ActionResult<List<ApiSpecFormatDto>> Formats()
            => Ok(_service.GetFormats());

        /// <summary>扫描目录下的 Controller 文件（含接口数预览）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public ActionResult<List<ApiSpecFileDto>> Controllers([FromQuery] string rootDir)
            => Ok(_service.ScanControllers(rootDir));

        /// <summary>解析选中 Controller → 接口清单 + DTO 类型树（前端预览面板）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public ActionResult<ApiSpecDocumentDto> Parse([FromQuery] string rootDir, [FromQuery] string files,
            [FromQuery] string? title, [FromQuery] string? baseUrl)
            => Ok(_service.Parse(rootDir, files, title, baseUrl));

        /// <summary>生成内容预览（返回字符串，不触发浏览器下载）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec")]
        public ActionResult<ApiSpecExportDto> Preview([FromQuery] string rootDir, [FromQuery] string files,
            [FromQuery] string format, [FromQuery] string? title, [FromQuery] string? baseUrl)
            => Ok(_service.Export(rootDir, files, format, title, baseUrl));

        /// <summary>下载生成的 API 数据文件（Content-Disposition 附件）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec:export")]
        public IActionResult Export([FromQuery] string rootDir, [FromQuery] string files,
            [FromQuery] string format, [FromQuery] string? title, [FromQuery] string? baseUrl)
        {
            var result = _service.Export(rootDir, files, format, title, baseUrl);
            var bytes = Encoding.UTF8.GetBytes(result.Content);
            return File(bytes, result.ContentType, result.FileName);
        }
    }
}
