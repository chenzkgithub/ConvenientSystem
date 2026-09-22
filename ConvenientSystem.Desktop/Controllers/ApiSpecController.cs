using System.Threading;
using System.Windows.Forms;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// API 文档生成器接口：扫描用户本机 C# 项目的 Controller 源码，生成 OpenAPI / Postman / Markdown
/// 等格式的 API 数据文件，供 Apifox / Postman 等工具导入。读的是本机文件系统，
/// 必须走桌面端本地控制器，不能转发到云端（云端容器读不到用户本机路径）。
/// </summary>
[ApiController]
// 本地接口新路由（接口分离）：与旧路由并存过渡，前端全部切换后移除旧路由
[Route("api/local/api-spec")]
[Route("api/Common/ApiSpec")]
public class ApiSpecController : ControllerBase
{
    private readonly ApiSpecService _service;
    private readonly ApiDebugService _debugService;

    public ApiSpecController(ApiSpecService service, ApiDebugService debugService)
    {
        _service = service;
        _debugService = debugService;
    }

    /// <summary>支持的导出格式列表（格式卡片网格数据源）。</summary>
    [HttpGet]
    [Route("Formats")]
    public IActionResult Formats()
        => Ok(_service.GetFormats());

    /// <summary>扫描目录下的 Controller 文件（含接口数预览）。</summary>
    [HttpGet]
    [Route("Controllers")]
    public IActionResult Controllers([FromQuery] string rootDir)
    {
        try { return Ok(_service.ScanControllers(rootDir)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>弹出本机文件选择对话框选择解决方案（.sln/.slnx），返回选中路径（取消返回 null）。</summary>
    [HttpGet]
    [Route("PickSolution")]
    public IActionResult PickSolution()
    {
        // OpenFileDialog 必须在 STA 线程上运行（ASP.NET 请求线程是 MTA）
        string? path = null;
        var thread = new Thread(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择解决方案文件",
                Filter = "解决方案文件 (*.sln;*.slnx)|*.sln;*.slnx|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                DereferenceLinks = true,
            };
            if (dialog.ShowDialog() == DialogResult.OK) path = dialog.FileName;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return Ok(new { path });
    }

    /// <summary>启动解决方案扫描后台任务：立即返回任务初始快照（含命名空间的接口清单在完成时随 Result 返回），进度查询走统一 AsyncTask/Get。</summary>
    [HttpPost]
    [Route("StartScan")]
    public IActionResult StartScan([FromBody] ApiSpecScanTaskRequest req)
    {
        try { return Ok(_service.StartScan(req, null)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>启动解析并生成后台任务：一次解析同时产出 IR 文档与导出内容，替代原 Parse+Preview 两次请求两次全量解析。</summary>
    [HttpPost]
    [Route("StartGenerate")]
    public IActionResult StartGenerate([FromBody] ApiSpecGenerateRequest req)
    {
        try { return Ok(_service.StartGenerate(req, null)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>复用已完成生成任务的解析结果重新导出（换格式/标题不重新解析源码，秒级返回）。</summary>
    [HttpPost]
    [Route("ReExport")]
    public IActionResult ReExport([FromBody] ApiSpecReExportRequest req)
    {
        try { return Ok(_service.ReExport(req, null)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>接口调试代理：服务端转发调试请求到目标地址并回传原始响应（规避浏览器 CORS）。</summary>
    [HttpPost]
    [Route("Debug")]
    public async Task<IActionResult> Debug([FromBody] ApiDebugRequest req)
    {
        try { return Ok(await _debugService.DebugAsync(req)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
