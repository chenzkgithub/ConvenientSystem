using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Hangfire 定时任务管理服务接口
    /// </summary>
    public interface IHangfireService
    {
        /// <summary>获取周期任务列表</summary>
        List<HangfireJobDto> GetRecurringJobs();

        /// <summary>手动触发一个周期任务</summary>
        void TriggerJob(string jobId);

        /// <summary>暂停/恢复周期任务</summary>
        void SetJobState(string jobId, bool paused);

        /// <summary>查询指定周期任务的执行历史（最近 50 次）</summary>
        List<HangfireExecutionLogDto> GetExecutionHistory(string recurringJobId);
    }
}
