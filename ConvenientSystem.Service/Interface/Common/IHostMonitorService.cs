using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 主机资源监控业务服务：监控目标增删改查、探测日志查询与手动立即检测。
    /// 定时巡检与状态告警由 HostMonitorCheckJob 负责，本服务仅管理配置与查询。
    /// </summary>
    public interface IHostMonitorService
    {
        /// <summary>查询全部监控目标（按创建时间倒序）</summary>
        List<HostMonitorTargetDto> List();

        /// <summary>新增或编辑监控目标，返回 Id</summary>
        int Save(HostMonitorTargetSaveDto dto);

        /// <summary>删除监控目标及其探测日志</summary>
        void Delete(int id);

        /// <summary>分页查询指定目标的探测日志（时间倒序）</summary>
        PagedResult<HostMonitorLogDto> GetLogs(int targetId, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>立即对指定目标执行一次探测（状态变化同样触发邮件告警），返回本次探测结果</summary>
        Task<HostMonitorLogDto> CheckNow(int id);

        /// <summary>监控健康度汇总（首页数据看板用）：按最近探测状态计数 + 异常目标明细</summary>
        MonitorHealthDto GetHealth();

        /// <summary>整机概览 Dashboard 数据（最新快照 + 时间序列历史点）</summary>
        HostMetricsHistoryDto GetMetrics(int targetId, int hours);

        /// <summary>启动指定整机概览目标磁盘扫描任务（后台异步，仅读取不删除），返回 jobId 供轮询进度</summary>
        string StartScan(int id, string? categories, string? drive);

        /// <summary>查询磁盘扫描任务进度/结果（未完成返回实时计数，完成后携带扫描结果）</summary>
        HostDiskScanJobDto GetScanProgress(string jobId);

        /// <summary>启动指定整机概览目标的磁盘清理任务（后台异步，本机或远程），返回 jobId 供轮询进度</summary>
        string StartClean(int id, HostDiskCleanRequestDto dto);

        /// <summary>查询磁盘清理任务进度/结果（未完成返回实时计数，完成后携带清理结果）</summary>
        HostDiskCleanJobDto GetCleanProgress(string jobId);

        /// <summary>在资源管理器中打开指定候选文件的所在文件夹（仅本机整机概览目标支持）</summary>
        Task OpenFolder(int id, string path);

        /// <summary>打开系统回收站（仅本机整机概览目标支持）</summary>
        Task OpenRecycleBin(int id);

        /// <summary>采集整机概览目标的设备规格与 ipconfig /all 网络信息</summary>
        Task<HostSystemInfoDto> GetSystemInfo(int id);

        /// <summary>提取文件扩展名对应的真实系统图标（ext → data URL；本机 Shell API / 远程 WinRM）</summary>
        Task<Dictionary<string, string>> GetFileIcons(int id, string exts);
    }
}
