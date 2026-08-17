namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>系统运行状态大盘</summary>
    public class SystemDashboardDto
    {
        // ---- 基本信息 ----
        public DateTime ServerTime { get; set; }
        public string MachineName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
        public int CpuCount { get; set; }

        // ---- 进程资源 ----
        public string ProcessName { get; set; } = "";
        public long WorkingSetMB { get; set; }
        public long PrivateMemoryMB { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public DateTime StartTime { get; set; }
        public long UptimeSeconds { get; set; }

        // ---- 磁盘空间 ----
        public long DiskTotalGB { get; set; }
        public long DiskFreeGB { get; set; }

        // ---- Hangfire 统计 ----
        public int HangfireEnqueued { get; set; }
        public int HangfireScheduled { get; set; }
        public int HangfireProcessing { get; set; }
        public int HangfireSucceeded { get; set; }
        public int HangfireFailed { get; set; }
        public int HangfireRecurring { get; set; }
        public List<HangfireServerDto> HangfireServers { get; set; } = new();
    }

    /// <summary>Hangfire 服务器信息</summary>
    public class HangfireServerDto
    {
        public string Name { get; set; } = "";
        public int WorkerCount { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? Heartbeat { get; set; }
    }

    /// <summary>Hangfire 周期任务信息</summary>
    public class HangfireJobDto
    {
        public string Id { get; set; } = "";
        public string Cron { get; set; } = "";
        public string? NextExecution { get; set; }
        public string? LastExecution { get; set; }
        public string? LastState { get; set; }
        public bool Paused { get; set; }
        public string? Queue { get; set; }
        public string? Description { get; set; }
    }
}
