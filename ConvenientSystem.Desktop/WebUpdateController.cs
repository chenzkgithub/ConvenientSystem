using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Desktop;

/// <summary>
/// Web 前端热更新接口：由页面内「发现新版本」提示条（UpdateBanner.vue）调用，
/// 替代启动期的静默更新（避免大包阻塞启动）。下载服务器激活版本并原子替换
/// 本地 wwwroot，完成后前端 reload 即生效。
/// </summary>
[ApiController]
[Route("api/Common/WebUpdate")]
public class WebUpdateController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebUpdateController> _logger;

    // 更新涉及 wwwroot 整体替换，同一时间只允许一个在跑（防重复点击并发下载）
    private static readonly SemaphoreSlim s_updateLock = new(1, 1);

    public WebUpdateController(IConfiguration configuration, ILogger<WebUpdateController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>下载并应用服务器激活的 Web 前端版本（幂等：无新版本时不做任何事）。</summary>
    [HttpPost]
    [Route("Apply")]
    public async Task<IActionResult> Apply()
    {
        if (!await s_updateLock.WaitAsync(0))
            return Conflict(new { message = "更新正在进行中，请稍候" });

        try
        {
            var remote = (_configuration["AppSettings:RemoteServerUrl"] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(remote))
                return Ok(new { updated = false });

            var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var remoteBaseUrl = $"http://{remote.TrimEnd('/')}";
            var updated = await WebUpdateService.SilentUpdateAsync(wwwrootDir, remoteBaseUrl);
            return Ok(new { updated });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web 前端热更新失败");
            return StatusCode(500, new { message = "更新失败：" + ex.Message });
        }
        finally
        {
            s_updateLock.Release();
        }
    }
}
