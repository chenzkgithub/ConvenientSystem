using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;
using Hangfire;
using Hangfire.Storage;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Hangfire 定时任务管理服务：查询周期任务、手动触发、暂停/恢复。
    /// </summary>
    public class HangfireService : IHangfireService
    {
        public List<HangfireJobDto> GetRecurringJobs()
        {
            try
            {
                using var conn = JobStorage.Current.GetConnection();
                var jobs = conn.GetRecurringJobs();
                if (jobs == null) return new List<HangfireJobDto>();

                return jobs.Select(j => new HangfireJobDto
                {
                    Id = j.Id,
                    Cron = j.Cron ?? "",
                    NextExecution = j.NextExecution?.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastExecution = j.LastExecution?.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastState = j.LastJobState,
                    Paused = j.NextExecution == null,
                    Queue = j.Queue,
                    Description = j.Job?.Type.Name,
                }).OrderBy(j => j.Id).ToList();
            }
            catch (Exception ex)
            {
                throw new BizException($"获取 Hangfire 任务失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void TriggerJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                throw new BadRequestException("任务 Id 不能为空");
            try
            {
                RecurringJob.TriggerJob(jobId);
            }
            catch (Exception ex)
            {
                throw new BizException($"触发任务失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void SetJobState(string jobId, bool paused)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                throw new BadRequestException("任务 Id 不能为空");
            try
            {
                if (paused)
                {
                    // 暂停：移除周期任务（Hangfire 不再调度）
                    RecurringJob.RemoveIfExists(jobId);
                }
                else
                {
                    // 恢复需要知道原始 cron，这里无法直接获取
                    throw new BadRequestException("恢复任务需要在系统中重新注册，暂不支持直接恢复");
                }
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"设置任务状态失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }
    }
}
