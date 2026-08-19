using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Sms;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Sms;
using Hangfire;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信任务业务服务实现；非管理员只能查看自己创建的任务。
    /// </summary>
    public class SmsTaskService : ISmsTaskService
    {
        private readonly IFreeSql _fsql;
        private readonly ISmsQuotaService _quotaService;
        private readonly ICurrentUser _currentUser;

        public SmsTaskService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ISmsQuotaService quotaService,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _quotaService = quotaService;
            _currentUser = currentUser;
        }

        public PagedResult<SmsTaskDto> GetList(byte? status, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            var query = _fsql.Select<SmsTaskEntity>();
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(t => t.CreatedById == _currentUser.UserId);
            if (status.HasValue) query = query.Where(t => t.Status == status.Value);

            var total = query.Count();
            var sortedQuery = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(t => t.CreateTime) : query.OrderByDynamic(sortField, sortOrder);
            var tasks = sortedQuery
                .Skip((page - 1) * size).Take(size)
                .ToList();
            // 创建人账号与姓名关联 SysUser 查询
            var userMap = UserDisplayHelper.GetMap(_fsql, tasks.Select(t => t.CreatedById));
            var list = tasks.Select(t => MapToDto(t, userMap)).ToList();

            return new PagedResult<SmsTaskDto> { Total = total, List = list };
        }

        public SmsTaskDetailDto Get(int id)
        {
            var task = _fsql.Select<SmsTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");

            var recipients = _fsql.Select<SmsRecipientEntity>()
                .Where(r => r.TaskId == id)
                .ToList()
                .Select(r => new SmsRecipientDto
                {
                    Id = r.Id,
                    TaskId = r.TaskId,
                    Phone = SmsPhoneHelper.Mask(r.Phone),
                    Name = r.Name,
                    Status = r.Status,
                    ErrorMessage = r.ErrorMessage,
                    SentTime = r.SentTime
                }).ToList();

            return new SmsTaskDetailDto
            {
                Task = MapToDto(task, UserDisplayHelper.GetMap(_fsql, new[] { task.CreatedById })),
                Recipients = recipients
            };
        }

        public SmsTaskCreatedDto Create(CreateSmsTaskRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) throw new BadRequestException("任务名称不能为空");
            if (req.TemplateId <= 0) throw new BadRequestException("请选择模板");
            if (req.SendTime <= DateTime.Now) throw new BadRequestException("发送时间必须晚于当前时间");
            if (req.Recipients == null || req.Recipients.Count == 0) throw new BadRequestException("请添加至少一个收件人");

            // 校验手机号格式
            foreach (var r in req.Recipients)
            {
                if (!SmsPhoneHelper.IsValid(r.Phone))
                    throw new BadRequestException($"手机号格式错误：{r.Phone}");
            }

            // 去重
            var distinctRecipients = req.Recipients
                .GroupBy(r => r.Phone)
                .Select(g => g.First())
                .ToList();

            // 检查配额
            var quotaCheck = _quotaService.CheckQuota(distinctRecipients.Count);
            if (!quotaCheck.ok)
                throw new BadRequestException(quotaCheck.message ?? "配额不足");

            // 创建任务
            var task = new SmsTaskEntity
            {
                Name = req.Name.Trim(),
                TemplateId = req.TemplateId,
                SendTime = req.SendTime,
                Status = 0,
                TotalCount = distinctRecipients.Count,
                CreatedById = _currentUser.UserId,
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now
            };
            var taskId = (int)_fsql.Insert(task).ExecuteIdentity();
            task.Id = taskId;

            // 批量插入收件人
            var recipients = distinctRecipients.Select(r => new SmsRecipientEntity
            {
                TaskId = taskId,
                Phone = r.Phone.Trim(),
                Name = (r.Name ?? string.Empty).Trim(),
                Status = 0
            }).ToList();
            _fsql.Insert(recipients).ExecuteAffrows();

            // Schedule 到 Hangfire
            var delay = req.SendTime - DateTime.Now;
            var jobId = BackgroundJob.Schedule<SmsSendJob>(
                job => job.SendAsync(taskId, default),
                delay);

            _fsql.Update<SmsTaskEntity>()
                .Set(t => t.HangfireJobId, jobId)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();

            return new SmsTaskCreatedDto { Id = taskId, HangfireJobId = jobId };
        }

        public void Cancel(int id)
        {
            var task = _fsql.Select<SmsTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");
            if (task.Status != 0) throw new BadRequestException("只能取消待执行状态的任务");

            DeleteHangfireJob(task.HangfireJobId);

            _fsql.Update<SmsTaskEntity>()
                .Set(t => t.Status, (byte)3)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == id)
                .ExecuteAffrows();
        }

        public void RetryFailed(int id)
        {
            var task = _fsql.Select<SmsTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");
            if (task.Status != 4) throw new BadRequestException("只能重发失败状态的任务");

            // 重置失败项为待发送
            _fsql.Update<SmsRecipientEntity>()
                .Set(r => r.Status, (byte)0)
                .Set(r => r.ErrorMessage, (string?)null)
                .Set(r => r.SentTime, (DateTime?)null)
                .Where(r => r.TaskId == id && r.Status == (byte)2)
                .ExecuteAffrows();

            // 重新 Schedule（立即执行）
            var jobId = BackgroundJob.Enqueue<SmsSendJob>(
                job => job.SendAsync(id, default));

            _fsql.Update<SmsTaskEntity>()
                .Set(t => t.Status, (byte)0)
                .Set(t => t.HangfireJobId, jobId)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == id)
                .ExecuteAffrows();
        }

        public void Delete(int id)
        {
            var task = _fsql.Select<SmsTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");
            if (task.Status == 1) throw new BadRequestException("执行中的任务不能删除");

            DeleteHangfireJob(task.HangfireJobId);

            _fsql.Delete<SmsLogEntity>().Where(l => l.TaskId == id).ExecuteAffrows();
            _fsql.Delete<SmsRecipientEntity>().Where(r => r.TaskId == id).ExecuteAffrows();
            _fsql.Delete<SmsTaskEntity>().Where(t => t.Id == id).ExecuteAffrows();
        }

        /// <summary>删除 Hangfire 作业（容错：Job 可能已执行完或不存在）</summary>
        private static void DeleteHangfireJob(string? jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return;
            try { BackgroundJob.Delete(jobId); } catch { }
        }

        private static SmsTaskDto MapToDto(SmsTaskEntity t, Dictionary<Guid, UserDisplayHelper.UserDisplay> userMap)
        {
            var creator = UserDisplayHelper.Find(userMap, t.CreatedById);
            return new SmsTaskDto
            {
                Id = t.Id,
                Name = t.Name,
                TemplateId = t.TemplateId,
                SendTime = t.SendTime,
                Status = t.Status,
                TotalCount = t.TotalCount,
                SuccessCount = t.SuccessCount,
                FailCount = t.FailCount,
                CreatedByAccount = creator?.Account,
                CreatedByName = creator?.DisplayName,
                CreateTime = t.CreateTime
            };
        }
    }
}
