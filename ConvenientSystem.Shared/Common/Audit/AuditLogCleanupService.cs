using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Audit
{
    /// <summary>
    /// 审计日志定时清理服务：每天执行一次，删除超过保留天数的审计日志，防止 SysAuditLog 无限增长。
    /// 保留天数从 SysConfig 表读取（AppSettings.AuditLogRetentionDays，缺省 60 天，非正数按 60 处理）。
    /// 任何异常都吞掉并记警告，绝不影响宿主运行。
    /// </summary>
    public class AuditLogCleanupService : BackgroundService
    {
        private const int DefaultRetentionDays = 60;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        private readonly IFreeSql _fsql;
        private readonly ILogger<AuditLogCleanupService> _logger;

        public AuditLogCleanupService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<AuditLogCleanupService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 启动后先跑一次，随后每 24 小时清理一次。
                do
                {
                    Cleanup();
                } while (await WaitNextAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // 正常停机
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志清理服务异常退出");
            }
        }

        /// <summary>从 SysConfig 表读取保留天数，DB 不可用或值无效时用缺省 60 天。</summary>
        private int GetRetentionDays()
        {
            try
            {
                var entity = _fsql.Select<SysConfigEntity>()
                    .Where(e => e.ConfigKey == "AppSettings.AuditLogRetentionDays")
                    .First();
                if (entity != null && int.TryParse(entity.ConfigValue, out var days) && days > 0)
                    return days;
            }
            catch { /* DB 不可用走缺省 */ }
            return DefaultRetentionDays;
        }

        private void Cleanup()
        {
            try
            {
                var retentionDays = GetRetentionDays();
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                var affected = _fsql.Delete<SysAuditLogEntity>()
                    .Where(l => l.CreateTime < cutoff)
                    .ExecuteAffrows();
                if (affected > 0)
                    _logger.LogInformation("审计日志清理完成，删除 {Count} 条（早于 {Cutoff:yyyy-MM-dd HH:mm:ss}，保留 {Days} 天）",
                        affected, cutoff, retentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志清理失败");
            }
        }

        private async Task<bool> WaitNextAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
