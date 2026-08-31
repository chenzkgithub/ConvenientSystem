using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 定时任务执行日志服务：直接操作 JobExecutionLog 表，供 Job 基类调用。
    /// </summary>
    public class JobExecutionLogService : IJobExecutionLogService
    {
        private readonly IFreeSql _fsql;

        public JobExecutionLogService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public long BeginLog(string jobName, string methodName, string? arguments)
        {
            var entity = new JobExecutionLogEntity
            {
                JobName = jobName,
                State = "Processing",
                MethodName = methodName,
                Arguments = arguments,
                StartedAt = DateTime.Now,
            };
            return _fsql.Insert(entity).ExecuteIdentity();
        }

        public void EndLog(long logId, string state, long durationMs, string? error)
        {
            _fsql.Update<JobExecutionLogEntity>()
                .Set(e => e.State, state)
                .Set(e => e.FinishedAt, DateTime.Now)
                .Set(e => e.DurationMs, durationMs)
                .Set(e => e.Error, error)
                .Where(e => e.Id == logId)
                .ExecuteAffrows();
        }
    }
}
