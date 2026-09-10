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
[Route("api/Common/ApiSpec")]
public class ApiSpecController : ControllerBase
{
    private readonly ApiSpecService _service;

    public ApiSpecController(ApiSpecService service)
    {
        _service = service;
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

    /// <summary>扫描解决方案（.sln/.slnx）或目录内全部接口，返回接口级清单。</summary>
    [HttpGet]
    [Route("ScanSolution")]
    public IActionResult ScanSolution([FromQuery] string solutionPath)
    {
        try { return Ok(_service.ScanSolution(solutionPath)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>解析选中 Controller → 接口清单 + DTO 类型树（前端预览面板）。</summary>
    [HttpGet]
    [Route("Parse")]
    public IActionResult Parse([FromQuery] string rootDir, [FromQuery] string files,
        [FromQuery] string? title, [FromQuery] string? baseUrl, [FromQuery] string? solutionPath)
    {
        try { return Ok(_service.Parse(rootDir, files, title, baseUrl, solutionPath)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>生成内容预览（下载由前端 Blob 生成，无需服务端附件接口）。选择标识放 body，避免接口多时 URL 过长导致 HTTP 414。</summary>
    [HttpPost]
    [Route("Preview")]
    public IActionResult Preview([FromBody] ApiSpecPreviewRequest req)
    {
        try { return Ok(_service.Export(req.RootDir, req.Files, req.Format, req.Title, req.BaseUrl,
            req.Only, req.SolutionPath, req.SelectionKeys)); }
        catch (BizException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
