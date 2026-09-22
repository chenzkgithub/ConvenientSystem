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
        private readonly IApiDebugService _debugService;

        public ApiSpecController(IApiSpecService service, IApiDebugService debugService)
        {
            _service = service;
            _debugService = debugService;
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

        /// <summary>启动解决方案扫描后台任务：立即返回任务初始快照（含命名空间的接口清单在完成时随 Result 返回）。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public ActionResult<AsyncTaskDto> StartScan([FromBody] ApiSpecScanTaskRequest req)
            => Ok(_service.StartScan(req, CurrentUserId));

        /// <summary>启动解析并生成后台任务：一次解析同时产出 IR 文档与导出内容，替代原 Parse+Preview 两次请求两次全量解析。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public ActionResult<AsyncTaskDto> StartGenerate([FromBody] ApiSpecGenerateRequest req)
            => Ok(_service.StartGenerate(req, CurrentUserId));

        /// <summary>复用已完成生成任务的解析结果重新导出（换格式/标题不重新解析源码，秒级返回）。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public ActionResult<ApiSpecExportDto> ReExport([FromBody] ApiSpecReExportRequest req)
            => Ok(_service.ReExport(req, CurrentUserId));

        /// <summary>下载生成的 API 数据文件（Content-Disposition 附件）。</summary>
        [HttpGet]
        [PermissionAuthorize("api-spec:export")]
        public IActionResult Export([FromQuery] string rootDir, [FromQuery] string files,
            [FromQuery] string format, [FromQuery] string? title, [FromQuery] string? baseUrl, [FromQuery] string? only)
        {
            var result = _service.Export(rootDir, files, format, title, baseUrl, only);
            var bytes = Encoding.UTF8.GetBytes(result.Content);
            return File(bytes, result.ContentType, result.FileName);
        }

        /// <summary>接口调试代理：服务端转发调试请求到目标地址并回传原始响应（规避浏览器 CORS）。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec:debug")]
        public async Task<ActionResult<ApiDebugResponse>> Debug([FromBody] ApiDebugRequest req)
            => Ok(await _debugService.DebugAsync(req));
    }
}
