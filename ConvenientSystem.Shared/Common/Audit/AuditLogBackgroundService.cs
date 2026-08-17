using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Audit
{
    /// <summary>
    /// 审计日志后台落库服务：消费 <see cref="AuditLogQueue"/>，批量写入配置库。
    /// 任何异常都吞掉并记警告，绝不影响主请求与宿主运行。
    /// </summary>
    public class AuditLogBackgroundService : BackgroundService
    {
        private readonly AuditLogQueue _queue;
        private readonly IFreeSql _fsql;
        private readonly ILogger<AuditLogBackgroundService> _logger;

        public AuditLogBackgroundService(
            AuditLogQueue queue,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<AuditLogBackgroundService> logger)
        {
            _queue = queue;
            _fsql = fsql;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reader = _queue.Reader;
            var batch = new List<SysAuditLogEntity>(64);
            try
            {
                while (await reader.WaitToReadAsync(stoppingToken))
                {
                    batch.Clear();
                    while (batch.Count < 200 && reader.TryRead(out var log))
                        batch.Add(log);

                    if (batch.Count == 0) continue;

                    try
                    {
                        await _fsql.Insert(batch).ExecuteAffrowsAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "审计日志批量落库失败，已丢弃 {Count} 条", batch.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停机
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志后台服务异常退出");
            }
        }
    }
}
