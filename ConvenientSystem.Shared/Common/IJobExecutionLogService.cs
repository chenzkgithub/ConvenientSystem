namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 定时任务执行日志服务：由各 Job 基类调用，记录任务执行的开始与结束。
    /// </summary>
    public interface IJobExecutionLogService
    {
        /// <summary>插入一条开始日志，返回日志 Id 供后续 EndLog 更新</summary>
        long BeginLog(string jobName, string methodName, string? arguments);

        /// <summary>更新日志为最终状态（Succeeded / Failed）并记录耗时与异常</summary>
        void EndLog(long logId, string state, long durationMs, string? error);
    }
}
