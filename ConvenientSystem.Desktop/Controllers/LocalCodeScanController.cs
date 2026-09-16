using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// 代码扫描本地控制器：扫描本机文件系统，规则存 exe 目录 code-scan-rules.json。
/// 与 ConvenientSystem.Api 中的 CodeScanController 接口一致，但不走权限校验。
/// </summary>
[ApiController]
[Route("api/Common/CodeScan")]
public class LocalCodeScanController : ControllerBase
{
    private readonly LocalCodeScanService _service;

    public LocalCodeScanController(LocalCodeScanService service)
    {
        _service = service;
    }

    /// <summary>获取所有检测规则。</summary>
    [HttpGet]
    [Route("ListRules")]
    public ActionResult<List<CodeScanRuleDto>> ListRules()
        => Ok(_service.ListRules());

    /// <summary>新建或更新规则。</summary>
    [HttpPost]
    [Route("SaveRule")]
    public IActionResult SaveRule([FromBody] CodeScanRuleSaveDto dto)
    {
        try
        {
            _service.SaveRule(dto);
            return Ok(new { message = "规则已保存" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>删除规则。</summary>
    [HttpDelete]
    [Route("DeleteRule")]
    public IActionResult DeleteRule([FromQuery] int id)
    {
        _service.DeleteRule(id);
        return Ok(new { message = "已删除" });
    }

    /// <summary>导入内置规则模板。</summary>
    [HttpPost]
    [Route("ImportTemplates")]
    public IActionResult ImportTemplates()
    {
        _service.ImportTemplates();
        return Ok(new { message = "模板导入完成" });
    }

    /// <summary>执行代码扫描。</summary>
    [HttpPost]
    [Route("Scan")]
    public ActionResult<CodeScanResultDto> Scan([FromBody] CodeScanRequestDto dto)
    {
        try
        {
            return Ok(_service.Scan(dto));
        }
        catch (DirectoryNotFoundException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>获取扫描历史。</summary>
    [HttpGet]
    [Route("ListResults")]
    public ActionResult<List<CodeScanResultDto>> ListResults([FromQuery] int limit = 20)
        => Ok(_service.ListResults(limit));

    /// <summary>获取某次扫描的详细结果。</summary>
    [HttpGet]
    [Route("GetResult")]
    public ActionResult<CodeScanResultDto> GetResult([FromQuery] int id)
    {
        var result = _service.GetResult(id);
        return result != null ? Ok(result) : NotFound();
    }
}
