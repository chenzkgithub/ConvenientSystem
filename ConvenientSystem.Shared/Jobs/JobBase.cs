using ConvenientSystem.Shared.Common;
using FreeSql;
using Newtonsoft.Json;
using System.Diagnostics;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 定时任务基类：提供 ExecuteWithLog 模板方法，统一记录任务执行日志。
    /// 子类继承后只需在 Hangfire 入口方法中用一行 ExecuteWithLog 包裹业务逻辑。
    /// </summary>
    public abstract class JobBase
    {
        protected readonly IFreeSql Fsql;
        private readonly IJobExecutionLogService _jobLog;

        protected JobBase(IFreeSql fsql, IJobExecutionLogService jobLog)
        {
            Fsql = fsql;
            _jobLog = jobLog;
        }

        /// <summary>
        /// 执行任务并记录日志（无返回值版本）。
        /// 子类只需一行调用：<c>await ExecuteWithLog("任务名", nameof(方法), 参数, () => 业务逻辑());</c>
        /// </summary>
        protected async Task ExecuteWithLog(string jobName, string methodName,
            object? arguments, Func<Task> execute)
        {
            var args = SerializeArgs(arguments);
            var logId = _jobLog.BeginLog(jobName, methodName, args);
            var sw = Stopwatch.StartNew();
            try
            {
                await execute();
                _jobLog.EndLog(logId, "Succeeded", sw.ElapsedMilliseconds, null);
            }
            catch (Exception ex)
            {
                _jobLog.EndLog(logId, "Failed", sw.ElapsedMilliseconds, ex.ToString());
                throw;
            }
        }

        /// <summary>
        /// 执行任务并记录日志（有返回值版本）。
        /// </summary>
        protected async Task<T> ExecuteWithLog<T>(string jobName, string methodName,
            object? arguments, Func<Task<T>> execute)
        {
            var args = SerializeArgs(arguments);
            var logId = _jobLog.BeginLog(jobName, methodName, args);
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await execute();
                _jobLog.EndLog(logId, "Succeeded", sw.ElapsedMilliseconds, null);
                return result;
            }
            catch (Exception ex)
            {
                _jobLog.EndLog(logId, "Failed", sw.ElapsedMilliseconds, ex.ToString());
                throw;
            }
        }

        private static string? SerializeArgs(object? arguments)
        {
            if (arguments == null) return null;
            try { return JsonConvert.SerializeObject(arguments); }
            catch { return arguments.ToString(); }
        }
    }
}
