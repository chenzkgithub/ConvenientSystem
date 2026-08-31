using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Controllers.Common;

/// <summary>
/// 桌面端本地监控控制器：在本机执行 PowerShell 采集指标，不经过云 API。
/// 路由 api/Common/Monitor/{action}，始终注册（无数据库依赖）。
/// </summary>
[ApiController]
[Route("api/[area]/[controller]/[action]")]
[Area("Common")]
[AllowAnonymous]
public class MonitorController(LocalMonitorService service) : ControllerBase
{
    /// <summary>整机概览：CPU/内存/磁盘/网络/IO/开机时长</summary>
    [HttpGet]
    public async Task<HostMetricsSnapshot> Overview()
        => await service.GetOverviewAsync(HttpContext.RequestAborted);

    /// <summary>设备规格与网络信息（ipconfig /all）</summary>
    [HttpGet]
    public async Task<HostSystemInfoDto> SystemInfo()
        => await service.GetSystemInfoAsync(HttpContext.RequestAborted);

    /// <summary>启动磁盘扫描任务（后台异步），返回 jobId；前端轮询 ScanProgress 获取进度</summary>
    [HttpGet]
    public HostDiskScanJobDto ScanDiskStart([FromQuery] string? categories, [FromQuery] string? drive)
        => new() { JobId = service.StartScan(categories, drive) };

    /// <summary>查询磁盘扫描任务进度/结果</summary>
    [HttpGet]
    public HostDiskScanJobDto ScanProgress([FromQuery] string jobId)
        => service.GetScanProgress(jobId);

    /// <summary>启动磁盘清理任务（后台异步），返回 jobId；前端轮询 CleanProgress 获取进度</summary>
    [HttpPost]
    public HostDiskCleanJobDto CleanDiskStart([FromBody] HostDiskCleanRequestDto dto)
        => new() { JobId = service.StartClean(dto), TotalCount = (dto.Paths?.Count ?? 0) + (dto.RecyclePaths?.Count ?? 0) };

    /// <summary>查询磁盘清理任务进度/结果</summary>
    [HttpGet]
    public HostDiskCleanJobDto CleanProgress([FromQuery] string jobId)
        => service.GetCleanProgress(jobId);

    /// <summary>在资源管理器中打开指定文件的所在文件夹并选中</summary>
    [HttpPost]
    public async Task OpenFolder([FromQuery] string path)
        => await service.OpenFolderAsync(path);

    /// <summary>打开系统回收站</summary>
    [HttpPost]
    public async Task OpenRecycleBin()
        => await service.OpenRecycleBinAsync();

    /// <summary>提取文件扩展名对应的真实系统图标（exts 逗号分隔，返回 ext → data URL）</summary>
    [HttpGet]
    public async Task<Dictionary<string, string>> FileIcons([FromQuery] string exts)
    {
        var list = (exts ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await service.GetFileIconsAsync(list);
    }
}
