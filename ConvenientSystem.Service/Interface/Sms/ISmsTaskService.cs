using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信任务业务服务：任务的创建（含 Hangfire 排程）、查询、取消、重发与删除。
    /// </summary>
    public interface ISmsTaskService
    {
        /// <summary>按状态分页查询任务列表</summary>
        PagedResult<SmsTaskDto> GetList(byte? status, int page, int size);

        /// <summary>查询任务详情（含收件人列表，手机号脱敏）；不存在时抛 NotFoundException</summary>
        SmsTaskDetailDto Get(int id);

        /// <summary>创建定时发送任务并排入 Hangfire，返回任务 Id 与作业 Id</summary>
        SmsTaskCreatedDto Create(CreateSmsTaskRequest req);

        /// <summary>取消待执行任务；任务不存在抛 NotFoundException，状态不允许抛 BadRequestException</summary>
        void Cancel(int id);

        /// <summary>重发失败任务（失败收件人重置为待发送并立即入队）</summary>
        void RetryFailed(int id);

        /// <summary>删除任务，连同收件人与日志一并删除；执行中的任务不允许删除</summary>
        void Delete(int id);
    }
}
