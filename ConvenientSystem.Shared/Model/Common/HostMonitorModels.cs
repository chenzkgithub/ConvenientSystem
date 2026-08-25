namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>主机监控指标类型</summary>
    public static class HostMonitorMetrics
    {
        /// <summary>磁盘（已用率%）</summary>
        public const string Disk = "DISK";
        /// <summary>内存（使用率%）</summary>
        public const string Memory = "MEM";
        /// <summary>CPU（使用率%）</summary>
        public const string Cpu = "CPU";
        /// <summary>Windows 服务运行状态</summary>
        public const string Service = "SVC";
        /// <summary>整机概览（一次采集 CPU/内存/磁盘/开机时长，支持本机或远程 IP）</summary>
        public const string Host = "HOST";

        /// <summary>指标类型中文名（用于展示与告警邮件）</summary>
        public static string Label(string type) => type switch
        {
            Disk => "磁盘",
            Memory => "内存",
            Cpu => "CPU",
            Service => "服务",
            Host => "整机概览",
            _ => type,
        };
    }

    /// <summary>整机概览指标快照（HOST 指标探测结果，序列化存入 MetricsJson）</summary>
    public class HostMetricsSnapshot
    {
        /// <summary>CPU 使用率%</summary>
        public decimal? CpuPercent { get; set; }
        /// <summary>CPU 逻辑核心数</summary>
        public int? CpuCores { get; set; }
        /// <summary>内存使用率%</summary>
        public decimal? MemoryPercent { get; set; }
        /// <summary>物理内存总量 GB</summary>
        public decimal? MemoryTotalGb { get; set; }
        /// <summary>已用内存 GB</summary>
        public decimal? MemoryUsedGb { get; set; }
        /// <summary>系统名称（Win32_OperatingSystem.Caption）</summary>
        public string? OsName { get; set; }
        /// <summary>开机时长（小时）</summary>
        public double? UptimeHours { get; set; }
        /// <summary>进程数</summary>
        public int? ProcessCount { get; set; }
        /// <summary>网络接收速率 KB/s</summary>
        public decimal? NetInKbps { get; set; }
        /// <summary>网络发送速率 KB/s</summary>
        public decimal? NetOutKbps { get; set; }
        /// <summary>磁盘读取速率 MB/s（_Total）</summary>
        public decimal? DiskReadMbPerSec { get; set; }
        /// <summary>磁盘写入速率 MB/s（_Total）</summary>
        public decimal? DiskWriteMbPerSec { get; set; }
        /// <summary>各固定磁盘使用情况</summary>
        public List<HostDiskUsage> Disks { get; set; } = new();
        /// <summary>采集时间</summary>
        public DateTime CheckedAt { get; set; }
    }

    /// <summary>整机概览中的单磁盘使用情况</summary>
    public class HostDiskUsage
    {
        /// <summary>盘符（如 C:）</summary>
        public string Drive { get; set; } = string.Empty;
        /// <summary>已用率%</summary>
        public decimal UsedPercent { get; set; }
        /// <summary>总容量 GB</summary>
        public decimal TotalGb { get; set; }
        /// <summary>剩余容量 GB</summary>
        public decimal FreeGb { get; set; }
    }

    /// <summary>磁盘清理请求 DTO：按勾选的清理项执行（至少勾选一项）</summary>
    public class HostDiskCleanRequestDto
    {
        /// <summary>目标盘符（单个大写字母 A-Z，默认 C）：临时目录类始终位于系统盘，
        /// 盘符只影响回收站清空与可用空间统计</summary>
        public string Drive { get; set; } = "C";
        /// <summary>用户临时目录（%TEMP%）</summary>
        public bool UserTemp { get; set; }
        /// <summary>Windows 临时目录（Windows\Temp）</summary>
        public bool WindowsTemp { get; set; }
        /// <summary>Prefetch 预读缓存</summary>
        public bool Prefetch { get; set; }
        /// <summary>Windows Update 下载缓存</summary>
        public bool UpdateCache { get; set; }
        /// <summary>浏览器缓存（Chrome/Edge/Firefox）</summary>
        public bool BrowserCache { get; set; }
        /// <summary>缩略图缓存（Explorer thumbcache）</summary>
        public bool ThumbnailCache { get; set; }
        /// <summary>日志文件（ProgramData 和 Temp 下的 *.log）</summary>
        public bool LogFiles { get; set; }
        /// <summary>旧下载文件（下载目录中超过指定天数未访问的文件）</summary>
        public bool OldDownloads { get; set; }
        /// <summary>磁盘垃圾（非系统盘显示的清理项：该盘垃圾扩展名文件 *.tmp/*.temp/*.log/*.bak/*.chk/*.old）</summary>
        public bool DriveJunk { get; set; }
        /// <summary>旧下载文件的天数阈值（默认 30 天）</summary>
        public int OldDownloadsDays { get; set; } = 30;
        /// <summary>清空所选盘的回收站</summary>
        public bool RecycleBin { get; set; }
        /// <summary>选定要删除的回收站条目原路径列表（勾选回收站时生效，仅删除勾选项）</summary>
        public List<string>? RecyclePaths { get; set; }
        /// <summary>选定要删除的文件路径列表（空 = 删除勾选项下全部文件）</summary>
        public List<string>? Paths { get; set; }

        /// <summary>勾选文件的扫描分类（路径→分类码，如 USER_TEMP）：选择性清理时按文件自身分类推导允许目录根，
        /// 避免依赖分类布尔标志重传导致合法文件被误判越界跳过</summary>
        public Dictionary<string, string>? PathCategories { get; set; }

        /// <summary>是否至少勾选了一项清理内容</summary>
        public bool HasAny => UserTemp || WindowsTemp || Prefetch || UpdateCache
            || BrowserCache || ThumbnailCache || LogFiles || OldDownloads || DriveJunk || RecycleBin;
    }

    /// <summary>磁盘可清理候选文件（扫描结果，仅读取不删除）</summary>
    public class HostDiskFileDto
    {
        /// <summary>所属分类：USER_TEMP/WIN_TEMP/PREFETCH/UPDATE_CACHE/BROWSER_CACHE/THUMBNAIL_CACHE/LOG_FILE/OLD_DOWNLOAD/RECYCLE</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>文件名</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>完整路径：过期文件为实际路径；回收站条目为 $Recycle.Bin 内物理路径（清理按它删除）</summary>
        public string Path { get; set; } = string.Empty;
        /// <summary>回收站条目原位置（删除来源目录+名称，仅 RECYCLE 填充，用于展示）</summary>
        public string OriginalPath { get; set; } = string.Empty;
        /// <summary>文件大小（KB）</summary>
        public decimal SizeKb { get; set; }
        /// <summary>最后修改时间</summary>
        public DateTime LastWriteTime { get; set; }
    }

    /// <summary>磁盘清理任务进度/结果 DTO（jobId 异步清理：前端轮询进度，完成后携带结果）</summary>
    public class HostDiskCleanJobDto
    {
        /// <summary>清理任务 ID</summary>
        public string JobId { get; set; } = string.Empty;
        /// <summary>清理是否已结束（成功或失败）</summary>
        public bool Done { get; set; }
        /// <summary>预计删除总数（启动时传入的勾选项计数，供前端算百分比；未知时为 0）</summary>
        public int TotalCount { get; set; }
        /// <summary>已删除条数（实时进度，逐 20 条上报）</summary>
        public int DeletedCount { get; set; }
        /// <summary>已释放空间（MB，实时进度）</summary>
        public decimal FreedMb { get; set; }
        /// <summary>清理失败原因（成功时为空）</summary>
        public string? Error { get; set; }
        /// <summary>清理完成后的完整结果（未完成时为空）</summary>
        public HostDiskCleanDto? Result { get; set; }
    }

    /// <summary>磁盘清理扫描结果 DTO</summary>
    public class HostDiskScanDto
    {
        /// <summary>候选文件列表（不含回收站，最多 3000 条）</summary>
        public List<HostDiskFileDto> Files { get; set; } = new();
        /// <summary>回收站项目数（勾选回收站时返回，为全部盘的合计）</summary>
        public int RecycleCount { get; set; }
        /// <summary>回收站内容约占用空间（KB）</summary>
        public decimal RecycleSizeKb { get; set; }
        /// <summary>回收站实时条目清单（不做有效期过滤，最多 3000 条，Category=RECYCLE）</summary>
        public List<HostDiskFileDto> RecycleFiles { get; set; } = new();
        /// <summary>候选文件超过扫描上限，列表已截断</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>磁盘扫描任务进度/结果 DTO（jobId 异步扫描：前端轮询进度，完成后携带结果）</summary>
    public class HostDiskScanJobDto
    {
        /// <summary>扫描任务 ID</summary>
        public string JobId { get; set; } = string.Empty;
        /// <summary>扫描是否已结束（成功或失败）</summary>
        public bool Done { get; set; }
        /// <summary>已扫描文件数（实时进度，来自 PowerShell 逐百条上报）</summary>
        public int ScannedCount { get; set; }
        /// <summary>已发现候选文件总大小（KB，实时进度）</summary>
        public decimal FoundKb { get; set; }
        /// <summary>扫描失败原因（成功时为空）</summary>
        public string? Error { get; set; }
        /// <summary>扫描完成后的完整结果（未完成时为空）</summary>
        public HostDiskScanDto? Result { get; set; }
    }

    /// <summary>磁盘临时文件清理结果 DTO</summary>
    public class HostDiskCleanDto
    {
        /// <summary>本次释放空间（MB）</summary>
        public decimal FreedMb { get; set; }
        /// <summary>清理后所选盘剩余空间（GB）</summary>
        public decimal FreeGbAfter { get; set; }
        /// <summary>成功删除的文件数（含回收站项目）</summary>
        public int DeletedFiles { get; set; }
        /// <summary>已删除文件路径列表（最多返回 2000 条）</summary>
        public List<string> Files { get; set; } = new();
        /// <summary>删除文件数超过列表上限时存在截断</summary>
        public bool FilesTruncated { get; set; }
        /// <summary>勾选逐项清理结果（选择性清理/回收站勾选时返回，整类清理为空）：每个勾选项的成功/失败及原因</summary>
        public List<HostDiskCleanItemDto> Items { get; set; } = new();
    }

    /// <summary>勾选单项清理结果</summary>
    public class HostDiskCleanItemDto
    {
        /// <summary>勾选的路径（回收站条目为 $Recycle.Bin 物理路径）</summary>
        public string Path { get; set; } = string.Empty;
        /// <summary>是否删除成功</summary>
        public bool Ok { get; set; }
        /// <summary>结果原因（已删除 / 文件不存在 / 不在勾选分类目录下 / 删除失败：xxx 等）</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>设备规格与网络信息 DTO（主机监控 Dashboard “设备规格/网络信息”面板）</summary>
    public class HostSystemInfoDto
    {
        /// <summary>设备名（计算机名）</summary>
        public string DeviceName { get; set; } = "";
        /// <summary>主板/机型（制造商 + 型号）</summary>
        public string Model { get; set; } = "";
        /// <summary>处理器（型号 + 主频）</summary>
        public string Processor { get; set; } = "";
        /// <summary>机带 RAM（总量 + 可用 + 频率）</summary>
        public string Ram { get; set; } = "";
        /// <summary>显卡（型号 + 显存）</summary>
        public string Gpu { get; set; } = "";
        /// <summary>存储（容量 + 型号）</summary>
        public string Storage { get; set; } = "";
        /// <summary>设备 ID</summary>
        public string DeviceId { get; set; } = "";
        /// <summary>产品 ID</summary>
        public string ProductId { get; set; } = "";
        /// <summary>系统类型（位数 + 处理器架构）</summary>
        public string SystemType { get; set; } = "";
        /// <summary>笔和触控支持情况</summary>
        public string PenTouch { get; set; } = "";
        /// <summary>ipconfig /all 原始输出</summary>
        public string NetworkText { get; set; } = "";
    }
}
