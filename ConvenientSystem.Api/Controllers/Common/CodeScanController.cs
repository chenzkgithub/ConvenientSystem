using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common;

/// <summary>
/// 代码扫描控制器：规则管理 + 执行扫描 + 查看结果。
/// </summary>
[Area("Common")]
public class CodeScanController : BaseController
{
    private readonly ICodeScanService _service;

    public CodeScanController(ICodeScanService service)
    {
        _service = service;
    }

    /// <summary>获取所有检测规则。</summary>
    [HttpGet]
    [PermissionAuthorize("code-scan")]
    public ActionResult<List<CodeScanRuleDto>> ListRules()
        => Ok(_service.ListRules());

    /// <summary>新建或更新规则。</summary>
    [HttpPost]
    [PermissionAuthorize("code-scan:edit")]
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
    [PermissionAuthorize("code-scan:edit")]
    public IActionResult DeleteRule([FromQuery] int id)
    {
        _service.DeleteRule(id);
        return Ok(new { message = "已删除" });
    }

    /// <summary>导入内置规则模板。</summary>
    [HttpPost]
    [PermissionAuthorize("code-scan:edit")]
    public IActionResult ImportTemplates()
    {
        _service.ImportTemplates();
        return Ok(new { message = "模板导入完成" });
    }

    /// <summary>执行代码扫描。</summary>
    [HttpPost]
    [PermissionAuthorize("code-scan:run")]
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

    /// <summary>获取扫描历史（最近 N 条）。</summary>
    [HttpGet]
    [PermissionAuthorize("code-scan")]
    public ActionResult<List<CodeScanResultDto>> ListResults([FromQuery] int limit = 20)
        => Ok(_service.ListResults(limit));

    /// <summary>获取某次扫描的详细结果。</summary>
    [HttpGet]
    [PermissionAuthorize("code-scan")]
    public ActionResult<CodeScanResultDto> GetResult([FromQuery] int id)
    {
        var result = _service.GetResult(id);
        return result != null ? Ok(result) : NotFound();
    }
}
