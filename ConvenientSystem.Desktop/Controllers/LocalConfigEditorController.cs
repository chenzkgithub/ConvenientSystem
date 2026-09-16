using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// 配置文件热编辑本地控制器：操作 exe 安装目录的配置文件。
/// 与 ConvenientSystem.Api 中的 ConfigEditorController 接口一致，
/// 但不走权限校验（本地功能，由 ReverseProxyMiddleware 白名单保证不转发到云）。
/// </summary>
[ApiController]
[Route("api/Common/ConfigEditor")]
public class LocalConfigEditorController : ControllerBase
{
    private readonly IConfigEditorService _service;

    public LocalConfigEditorController(IConfigEditorService service)
    {
        _service = service;
    }

    /// <summary>获取可编辑的配置文件列表。</summary>
    [HttpGet]
    [Route("List")]
    public ActionResult<List<ConfigFileInfoDto>> List()
        => Ok(_service.ListFiles());

    /// <summary>读取指定配置文件的完整内容。</summary>
    [HttpGet]
    [Route("Read")]
    public ActionResult<ConfigFileContentDto> Read([FromQuery] string name)
    {
        try
        {
            return Ok(_service.ReadFile(name));
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>保存配置文件（自动备份旧版本）。</summary>
    [HttpPost]
    [Route("Save")]
    public IActionResult Save([FromBody] ConfigFileSaveDto dto)
    {
        try
        {
            _service.SaveFile(dto.Name, dto.Content);
            return Ok(new { message = "保存成功" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>触发服务重启。</summary>
    [HttpPost]
    [Route("Restart")]
    public IActionResult Restart()
    {
        _service.TriggerRestart();
        return Ok(new { message = "服务正在重启..." });
    }

    /// <summary>获取服务状态。</summary>
    [HttpGet]
    [Route("Status")]
    public ActionResult<ConfigEditorStatusDto> Status()
        => Ok(_service.GetStatus());
}
