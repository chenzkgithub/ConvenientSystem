using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 主机资源监控接口：监控目标管理、探测日志查询与手动立即检测（"主机监控"菜单专用）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("host-monitor")]
    public class HostMonitorController : BaseController
    {
        private readonly IHostMonitorService _hostMonitorService;

        public HostMonitorController(IHostMonitorService hostMonitorService)
        {
            _hostMonitorService = hostMonitorService;
        }

        /// <summary>查询全部监控目标（含最近探测状态）</summary>
        [HttpGet]
        public ActionResult<List<HostMonitorTargetDto>> List()
            => Ok(_hostMonitorService.List());

        /// <summary>新增或编辑监控目标（Id 为空表示新增）</summary>
        [HttpPost]
        [PermissionAuthorize("host-monitor:create", "host-monitor:edit")]
        public ActionResult<int> Save([FromBody] HostMonitorTargetSaveDto dto)
            => Ok(_hostMonitorService.Save(dto));

        /// <summary>删除监控目标及其探测日志</summary>
        [HttpDelete]
        [PermissionAuthorize("host-monitor:delete")]
        public ActionResult Delete([FromQuery] int id)
        {
            _hostMonitorService.Delete(id);
            return Ok();
        }

        /// <summary>分页查询指定目标的探测日志（时间倒序）</summary>
        [HttpGet]
        public ActionResult<PagedResult<HostMonitorLogDto>> Logs(
            [FromQuery] int targetId,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
            => Ok(_hostMonitorService.GetLogs(targetId, page, size));

        /// <summary>立即对指定目标执行一次探测，返回本次探测结果</summary>
        [HttpPost]
        [PermissionAuthorize("host-monitor:check")]
        public async Task<ActionResult<HostMonitorLogDto>> Check([FromQuery] int id)
            => Ok(await _hostMonitorService.CheckNow(id));

        /// <summary>监控健康度汇总（首页数据看板用）</summary>
        [HttpGet]
        public ActionResult<MonitorHealthDto> Health()
            => Ok(_hostMonitorService.GetHealth());

        /// <summary>整机概览 Dashboard 数据（最新快照 + 时间序列历史点，hours 默认 6）</summary>
        [HttpGet]
        public ActionResult<HostMetricsHistoryDto> Metrics(
            [FromQuery] int targetId,
            [FromQuery] int hours = 6)
            => Ok(_hostMonitorService.GetMetrics(targetId, hours));

        /// <summary>启动指定整机概览目标磁盘扫描任务（后台异步，仅读取不删除），返回 jobId；前端轮询 ScanProgress 获取实时进度与结果</summary>
        [HttpGet]
        public ActionResult<HostDiskScanJobDto> ScanDiskStart([FromQuery] int id, [FromQuery] string? categories, [FromQuery] string? drive)
            => Ok(new HostDiskScanJobDto { JobId = _hostMonitorService.StartScan(id, categories, drive) });

        /// <summary>查询磁盘扫描任务进度/结果（未完成返回实时计数，完成后携带扫描结果或失败原因）</summary>
        [HttpGet]
        public ActionResult<HostDiskScanJobDto> ScanProgress([FromQuery] string jobId)
            => Ok(_hostMonitorService.GetScanProgress(jobId));

        /// <summary>启动指定整机概览目标的磁盘清理任务（后台异步，本机或远程），返回 jobId；前端轮询 CleanProgress 获取实时进度与结果</summary>
        [HttpPost]
        [PermissionAuthorize("host-monitor:clean-disk")]
        public ActionResult<HostDiskCleanJobDto> CleanDiskStart([FromQuery] int id, [FromBody] HostDiskCleanRequestDto dto)
            => Ok(new HostDiskCleanJobDto { JobId = _hostMonitorService.StartClean(id, dto), TotalCount = (dto.Paths?.Count ?? 0) + (dto.RecyclePaths?.Count ?? 0) });

        /// <summary>查询磁盘清理任务进度/结果（未完成返回实时计数，完成后携带清理结果或失败原因）</summary>
        [HttpGet]
        public ActionResult<HostDiskCleanJobDto> CleanProgress([FromQuery] string jobId)
            => Ok(_hostMonitorService.GetCleanProgress(jobId));

        /// <summary>在资源管理器中打开指定候选文件的所在文件夹并选中文件（仅本机目标）</summary>
        [HttpPost]
        public async Task<ActionResult> OpenFolder([FromQuery] int id, [FromQuery] string path)
        {
            await _hostMonitorService.OpenFolder(id, path);
            return Ok();
        }

        /// <summary>打开系统回收站（仅本机目标）</summary>
        [HttpPost]
        public async Task<ActionResult> OpenRecycleBin([FromQuery] int id)
        {
            await _hostMonitorService.OpenRecycleBin(id);
            return Ok();
        }

        /// <summary>采集整机概览目标的设备规格（设备名/处理器/内存/显卡/存储等）与 ipconfig /all 网络信息</summary>
        [HttpGet]
        public async Task<ActionResult<HostSystemInfoDto>> SystemInfo([FromQuery] int id)
            => Ok(await _hostMonitorService.GetSystemInfo(id));

        /// <summary>提取文件扩展名对应的真实系统图标（与资源管理器展示一致；exts 逗号分隔，返回 ext → data URL 映射）</summary>
        [HttpGet]
        public async Task<ActionResult<Dictionary<string, string>>> FileIcons([FromQuery] int id, [FromQuery] string exts)
            => Ok(await _hostMonitorService.GetFileIcons(id, exts));
    }
}
