using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// Hosts 管理本地控制器：读写本机 hosts 文件（解析/备份/还原）。
/// 仅存在于桌面端内嵌 Kestrel；浏览器直连远程服务器时该端点不存在（前端据此提示"仅桌面端本机可用"）。
/// 不走权限校验（本地功能，由 ReverseProxyMiddleware 白名单保证不转发到云）。
/// </summary>
[ApiController]
// 本地接口新路由（接口分离）：与旧路由并存过渡，前端全部切换后移除旧路由
[Route("api/local/hosts")]
[Route("api/Common/Hosts")]
public class LocalHostsController : ControllerBase
{
    private readonly HostsFileService _service;

    public LocalHostsController(HostsFileService service)
    {
        _service = service;
    }

    /// <summary>读取并解析本机 hosts 文件。</summary>
    [HttpGet]
    [Route("List")]
    public ActionResult<HostsFileDto> List()
    {
        try
        {
            return Ok(_service.List());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = $"读取 hosts 文件失败：{ex.Message}" });
        }
    }

    /// <summary>整体写回 hosts（写入前自动备份，保留最近 20 份）。</summary>
    [HttpPost]
    [Route("Save")]
    public IActionResult Save([FromBody] HostsSaveDto dto)
    {
        try
        {
            var backupName = _service.Save(dto);
            return Ok(new { message = "已保存并生效", backup = backupName });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "写入 hosts 需要管理员权限，请以管理员身份重启本程序后再试" });
        }
        catch (IOException ex)
        {
            return Conflict(new { message = $"hosts 文件被占用，写入失败：{ex.Message}" });
        }
    }

    /// <summary>备份列表（新的在前）。</summary>
    [HttpGet]
    [Route("Backups")]
    public ActionResult<List<HostsBackupDto>> Backups()
    {
        try
        {
            return Ok(_service.ListBackups());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = $"读取备份列表失败：{ex.Message}" });
        }
    }

    /// <summary>用指定备份还原 hosts（还原前自动备份当前版本）。</summary>
    [HttpPost]
    [Route("Restore")]
    public IActionResult Restore([FromBody] HostsBackupDto dto)
    {
        try
        {
            var currentBackup = _service.Restore(dto.Name);
            return Ok(new { message = "已还原", backup = currentBackup });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "写入 hosts 需要管理员权限，请以管理员身份重启本程序后再试" });
        }
        catch (IOException ex)
        {
            return Conflict(new { message = $"hosts 文件被占用，还原失败：{ex.Message}" });
        }
    }
}
