using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Email;
using Hangfire;

namespace ConvenientSystem.Service.Email
{
    /// <summary>
    /// 邮件定时任务业务服务实现。
    /// </summary>
    public class EmailTaskService : IEmailTaskService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public EmailTaskService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        public List<EmailTaskDto> GetList()
        {
            var query = _fsql.Select<EmailTaskEntity>();
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(t => t.CreatedById == _currentUser.UserId);

            var tasks = query.OrderByDescending(t => t.CreateTime).ToList();
            // 创建人账号与姓名关联 SysUser 查询
            var userMap = UserDisplayHelper.GetMap(_fsql, tasks.Select(t => t.CreatedById));
            return tasks.Select(t => MapToDto(t, userMap)).ToList();
        }

        public EmailTaskDto Get(int id)
        {
            var task = _fsql.Select<EmailTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");
            var userMap = UserDisplayHelper.GetMap(_fsql, new[] { task.CreatedById });
            return MapToDto(task, userMap);
        }

        public EmailTaskCreatedDto Create(EmailTaskDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new BadRequestException("任务名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.Subject)) throw new BadRequestException("邮件主题不能为空");
            if (string.IsNullOrWhiteSpace(dto.Content)) throw new BadRequestException("邮件内容不能为空");
            if (string.IsNullOrWhiteSpace(dto.Recipients)) throw new BadRequestException("收件人不能为空");

            var entity = new EmailTaskEntity
            {
                Name = dto.Name.Trim(),
                Subject = dto.Subject.Trim(),
                Content = dto.Content.Trim(),
                Recipients = dto.Recipients.Trim(),
                ScheduleType = dto.ScheduleType ?? "once",
                SendTime = dto.SendTime,
                CronExpression = dto.CronExpression,
                WeekDays = dto.WeekDays,
                DailyTime = dto.DailyTime,
                Enabled = true,
                Status = 0,
                CreatedById = _currentUser.UserId,
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now
            };

            var taskId = (int)_fsql.Insert(entity).ExecuteIdentity();
            entity.Id = taskId;

            // 注册 Hangfire 定时任务
            var jobId = ScheduleHangfireJob(entity);
            if (!string.IsNullOrEmpty(jobId))
            {
                SaveJobId(taskId, jobId);
            }

            return new EmailTaskCreatedDto { Id = taskId, HangfireJobId = jobId };
        }

        public void Update(EmailTaskDto dto)
        {
            var existing = _fsql.Select<EmailTaskEntity>().Where(t => t.Id == dto.Id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && existing.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");

            // 先删除旧的 Hangfire Job
            RemoveHangfireJob(existing.HangfireJobId, existing.ScheduleType);

            // 更新数据库
            _fsql.Update<EmailTaskEntity>()
                .Set(t => t.Name, dto.Name.Trim())
                .Set(t => t.Subject, dto.Subject.Trim())
                .Set(t => t.Content, dto.Content.Trim())
                .Set(t => t.Recipients, dto.Recipients.Trim())
                .Set(t => t.ScheduleType, dto.ScheduleType)
                .Set(t => t.SendTime, dto.SendTime)
                .Set(t => t.CronExpression, dto.CronExpression)
                .Set(t => t.WeekDays, dto.WeekDays)
                .Set(t => t.DailyTime, dto.DailyTime)
                .Set(t => t.Enabled, dto.Enabled)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == dto.Id)
                .ExecuteAffrows();

            // 重新注册 Hangfire Job
            existing.Name = dto.Name.Trim();
            existing.Subject = dto.Subject.Trim();
            existing.Content = dto.Content.Trim();
            existing.Recipients = dto.Recipients.Trim();
            existing.ScheduleType = dto.ScheduleType ?? "once";
            existing.SendTime = dto.SendTime;
            existing.CronExpression = dto.CronExpression;
            existing.WeekDays = dto.WeekDays;
            existing.DailyTime = dto.DailyTime;
            existing.Enabled = dto.Enabled;

            var jobId = ScheduleHangfireJob(existing);
            if (!string.IsNullOrEmpty(jobId))
            {
                SaveJobId(dto.Id, jobId);
            }
        }

        public EmailTaskToggleDto Toggle(int id)
        {
            var task = _fsql.Select<EmailTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");

            var newEnabled = !task.Enabled;
            _fsql.Update<EmailTaskEntity>()
                .Set(t => t.Enabled, newEnabled)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == id)
                .ExecuteAffrows();

            if (!newEnabled)
            {
                // 禁用时删除 Hangfire Job
                RemoveHangfireJob(task.HangfireJobId, task.ScheduleType);
                _fsql.Update<EmailTaskEntity>()
                    .Set(t => t.HangfireJobId, (string?)null)
                    .Where(t => t.Id == id)
                    .ExecuteAffrows();
            }
            else
            {
                // 启用时重新注册（须先同步实体的启用状态，否则 ScheduleHangfireJob 会直接返回 null）
                task.Enabled = true;
                var jobId = ScheduleHangfireJob(task);
                if (!string.IsNullOrEmpty(jobId))
                {
                    SaveJobId(id, jobId);
                }
            }

            return new EmailTaskToggleDto { Enabled = newEnabled };
        }

        public void Delete(int id)
        {
            var task = _fsql.Select<EmailTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");

            RemoveHangfireJob(task.HangfireJobId, task.ScheduleType);

            _fsql.Delete<EmailLogEntity>().Where(l => l.TaskId == id).ExecuteAffrows();
            _fsql.Delete<EmailTaskEntity>().Where(t => t.Id == id).ExecuteAffrows();
        }

        public EmailTaskRunNowDto RunNow(int id)
        {
            var task = _fsql.Select<EmailTaskEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("任务不存在");
            if (_currentUser.DataScope != DataScope.All && task.CreatedById != _currentUser.UserId)
                throw new NotFoundException("任务不存在");

            var jobId = BackgroundJob.Enqueue<EmailSendJob>(
                job => job.SendAsync(id, default));

            return new EmailTaskRunNowDto { HangfireJobId = jobId };
        }

        public PagedResult<EmailLogDto> GetLogs(int? taskId, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            var query = _fsql.Select<EmailLogEntity>();
            if (taskId.HasValue) query = query.Where(l => l.TaskId == taskId.Value);

            // 数据范围为本人时只能查看自己创建的任务对应的日志
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
            {
                var ownedTaskIds = _fsql.Select<EmailTaskEntity>()
                    .Where(t => t.CreatedById == _currentUser.UserId)
                    .ToList(t => t.Id);
                query = query.Where(l => ownedTaskIds.Contains(l.TaskId));
            }

            var total = query.Count();
            var sortedQuery = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(l => l.CreateTime) : query.OrderByDynamic(sortField, sortOrder);
            var logs = sortedQuery
                .Skip((page - 1) * size).Take(size)
                .ToList();
            // 创建人账号与姓名关联 SysUser 查询（系统自动发送的日志无创建人）
            var userMap = UserDisplayHelper.GetMap(_fsql, logs.Select(l => l.CreatedById));
            var list = logs.Select(l =>
                {
                    var creator = UserDisplayHelper.Find(userMap, l.CreatedById);
                    return new EmailLogDto
                    {
                        Id = l.Id,
                        TaskId = l.TaskId,
                        TaskName = l.TaskName,
                        Recipients = l.Recipients,
                        Subject = l.Subject,
                        Content = l.Content,
                        Status = l.Status,
                        ErrorMessage = l.ErrorMessage,
                        CostMs = l.CostMs,
                        CreatedByAccount = creator?.Account ?? "系统",
                        CreatedByName = creator?.DisplayName,
                        CreateTime = l.CreateTime
                    };
                }).ToList();

            return new PagedResult<EmailLogDto> { Total = total, List = list };
        }

        public SendTrendDto GetTrend(int days)
        {
            // 归一化天数区间（1~90 天），起点为今天往前推 days-1 天的零点
            if (days < 1) days = 1;
            if (days > 90) days = 90;
            var startDate = DateTime.Today.AddDays(-(days - 1));
            var endExclusive = DateTime.Today.AddDays(1);

            var query = _fsql.Select<EmailLogEntity>()
                .Where(l => l.CreateTime >= startDate && l.CreateTime < endExclusive);

            // 数据范围为本人时只统计自己创建的任务下的日志，与 GetLogs 保持一致
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
            {
                var ownedTaskIds = _fsql.Select<EmailTaskEntity>()
                    .Where(t => t.CreatedById == _currentUser.UserId)
                    .ToList(t => t.Id);
                query = query.Where(l => ownedTaskIds.Contains(l.TaskId));
            }

            // 邮件日志 Status==1 视为成功，其余视为失败
            var rows = query.ToList(l => new { l.CreateTime, l.Status });
            return TrendBuilder.Build(startDate, days, rows.Select(r => (r.CreateTime, r.Status == 1)));
        }

        /// <summary>回写 Hangfire 作业 Id</summary>
        private void SaveJobId(int taskId, string jobId)
        {
            _fsql.Update<EmailTaskEntity>()
                .Set(t => t.HangfireJobId, jobId)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();
        }

        /// <summary>移除 Hangfire 作业（容错：作业可能已执行完或不存在）</summary>
        private static void RemoveHangfireJob(string? jobId, string? scheduleType)
        {
            if (string.IsNullOrEmpty(jobId)) return;
            try
            {
                if (scheduleType == "once")
                    BackgroundJob.Delete(jobId);
                else
                    RecurringJob.RemoveIfExists(jobId);
            }
            catch { }
        }

        /// <summary>根据调度类型注册 Hangfire Job，返回 Job ID</summary>
        private static string? ScheduleHangfireJob(EmailTaskEntity task)
        {
            if (!task.Enabled) return null;

            switch (task.ScheduleType)
            {
                case "once":
                    if (task.SendTime.HasValue && task.SendTime.Value > DateTime.Now)
                    {
                        var delay = task.SendTime.Value - DateTime.Now;
                        return BackgroundJob.Schedule<EmailSendJob>(
                            job => job.SendAsync(task.Id, default), delay);
                    }
                    else if (task.SendTime.HasValue)
                    {
                        // 时间已过，立即执行
                        return BackgroundJob.Enqueue<EmailSendJob>(
                            job => job.SendAsync(task.Id, default));
                    }
                    return null;

                case "daily":
                    {
                        var time = ParseDailyTime(task.DailyTime);
                        var cron = $"{time.Minutes} {time.Hours} * * *";
                        var jobId = $"邮件-{task.Name}";
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobId,
                            job => job.SendAsync(task.Id, default),
                            cron,
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobId;
                    }

                case "weekly":
                    {
                        var time = ParseDailyTime(task.DailyTime);
                        var days = ParseWeekDays(task.WeekDays);
                        var daysStr = string.Join(",", days);
                        var cron = $"{time.Minutes} {time.Hours} * * {daysStr}";
                        var jobId = $"邮件-{task.Name}";
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobId,
                            job => job.SendAsync(task.Id, default),
                            cron,
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobId;
                    }

                case "cron":
                    if (!string.IsNullOrWhiteSpace(task.CronExpression))
                    {
                        var jobId = $"邮件-{task.Name}";
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobId,
                            job => job.SendAsync(task.Id, default),
                            task.CronExpression.Trim(),
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobId;
                    }
                    return null;

                default:
                    return null;
            }
        }

        private static TimeSpan ParseDailyTime(string? dailyTime)
        {
            if (string.IsNullOrWhiteSpace(dailyTime)) return new TimeSpan(9, 0, 0);
            var parts = dailyTime.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                return new TimeSpan(h, m, 0);
            return new TimeSpan(9, 0, 0);
        }

        private static List<int> ParseWeekDays(string? weekDays)
        {
            if (string.IsNullOrWhiteSpace(weekDays)) return [1, 2, 3, 4, 5]; // 默认工作日
            return weekDays.Split(',')
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();
        }

        private static EmailTaskDto MapToDto(EmailTaskEntity t, Dictionary<Guid, UserDisplayHelper.UserDisplay> userMap)
        {
            var creator = UserDisplayHelper.Find(userMap, t.CreatedById);
            return new EmailTaskDto
            {
                Id = t.Id,
                Name = t.Name,
                Subject = t.Subject,
                Content = t.Content,
                Recipients = t.Recipients,
                ScheduleType = t.ScheduleType,
                SendTime = t.SendTime,
                CronExpression = t.CronExpression,
                WeekDays = t.WeekDays,
                DailyTime = t.DailyTime,
                Enabled = t.Enabled,
                Status = t.Status,
                LastSendTime = t.LastSendTime,
                CreatedByAccount = creator?.Account,
                CreatedByName = creator?.DisplayName,
                CreateTime = t.CreateTime
            };
        }
    }
}
