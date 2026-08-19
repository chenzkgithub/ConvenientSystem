using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Email;

namespace ConvenientSystem.Service.Email
{
    /// <summary>
    /// 邮件定时任务业务服务：任务增删改、启停、立即执行与发送日志查询。
    /// 所有写操作都会同步维护对应的 Hangfire 作业。
    /// </summary>
    public interface IEmailTaskService
    {
        /// <summary>查询全部任务</summary>
        List<EmailTaskDto> GetList();

        /// <summary>查询单个任务；不存在时抛 NotFoundException</summary>
        EmailTaskDto Get(int id);

        /// <summary>创建任务并注册 Hangfire 作业</summary>
        EmailTaskCreatedDto Create(EmailTaskDto dto);

        /// <summary>更新任务并重建 Hangfire 作业；不存在时抛 NotFoundException</summary>
        void Update(EmailTaskDto dto);

        /// <summary>切换启用状态：禁用时移除作业，启用时重新注册</summary>
        EmailTaskToggleDto Toggle(int id);

        /// <summary>删除任务（连同发送日志与 Hangfire 作业）</summary>
        void Delete(int id);

        /// <summary>立即执行一次，返回入队的作业 Id</summary>
        EmailTaskRunNowDto RunNow(int id);

        /// <summary>分页查询发送日志</summary>
        PagedResult<EmailLogDto> GetLogs(int? taskId, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>按日发送趋势（含成功/失败），受数据权限过滤；days 为往前天数（含今天）</summary>
        SendTrendDto GetTrend(int days);
    }
}
