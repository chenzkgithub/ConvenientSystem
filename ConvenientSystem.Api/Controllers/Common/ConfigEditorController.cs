using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common;

/// <summary>
/// 配置文件热编辑控制器：列出、读取、保存配置文件，触发服务重启。
/// 所有端点均需要 "config-editor" 系列权限点。
/// </summary>
[Area("Common")]
public class ConfigEditorController : BaseController
{
    private readonly IConfigEditorService _service;

    public ConfigEditorController(IConfigEditorService service)
    {
        _service = service;
    }

    /// <summary>获取可编辑的配置文件列表。</summary>
    [HttpGet]
    [PermissionAuthorize("config-editor")]
    public ActionResult<List<ConfigFileInfoDto>> List()
        => Ok(_service.ListFiles());

    /// <summary>读取指定配置文件的完整内容。</summary>
    [HttpGet]
    [PermissionAuthorize("config-editor")]
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
    [PermissionAuthorize("config-editor:save")]
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

    /// <summary>触发服务重启（配置文件变更后调用）。</summary>
    [HttpPost]
    [PermissionAuthorize("config-editor:restart")]
    public IActionResult Restart()
    {
        _service.TriggerRestart();
        return Ok(new { message = "服务正在重启..." });
    }

    /// <summary>获取服务状态（启动时间等）。</summary>
    [HttpGet]
    [PermissionAuthorize("config-editor")]
    public ActionResult<ConfigEditorStatusDto> Status()
        => Ok(_service.GetStatus());
}
