using ConvenientSystem.Shared.Model.Common;
using Hangfire.Storage;
using System.Diagnostics;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统运行状态大盘服务：采集进程资源、Hangfire 任务统计、磁盘空间。
    /// </summary>
    public class SystemDashboardService : ISystemDashboardService
    {
        public SystemDashboardDto GetDashboard()
        {
            var process = Process.GetCurrentProcess();
            var dto = new SystemDashboardDto
            {
                ServerTime = DateTime.Now,
                ProcessName = process.ProcessName,
                WorkingSetMB = (long)(process.WorkingSet64 / 1024.0 / 1024.0),
                PrivateMemoryMB = (long)(process.PrivateMemorySize64 / 1024.0 / 1024.0),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                StartTime = process.StartTime,
                UptimeSeconds = (long)(DateTime.Now - process.StartTime).TotalSeconds,
                CpuCount = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
            };

            // Hangfire 任务统计
            try
            {
                var monitoring = Hangfire.JobStorage.Current.GetMonitoringApi();
                var stats = monitoring.GetStatistics();
                dto.HangfireEnqueued = (int)stats.Enqueued;
                dto.HangfireScheduled = (int)stats.Scheduled;
                dto.HangfireProcessing = (int)stats.Processing;
                dto.HangfireSucceeded = (int)stats.Succeeded;
                dto.HangfireFailed = (int)stats.Failed;
                using var conn = Hangfire.JobStorage.Current.GetConnection();
                dto.HangfireRecurring = conn.GetRecurringJobs()?.Count ?? 0;
                dto.HangfireServers = monitoring.Servers()?.Select(s => new HangfireServerDto
                {
                    Name = s.Name,
                    WorkerCount = s.WorkersCount,
                    StartedAt = s.StartedAt,
                    Heartbeat = s.Heartbeat,
                }).ToList() ?? new List<HangfireServerDto>();
            }
            catch { /* Hangfire 未初始化时忽略 */ }

            // 磁盘空间
            try
            {
                var root = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                dto.DiskTotalGB = (long)(drive.TotalSize / 1024.0 / 1024.0 / 1024.0);
                dto.DiskFreeGB = (long)(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0);
            }
            catch { }

            return dto;
        }
    }
}
