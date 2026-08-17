using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Sms;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信发送日志业务服务实现；非管理员只能查看自己任务下的发送日志。
    /// </summary>
    public class SmsLogService : ISmsLogService
    {
        private readonly IFreeSql _fsql;
        private readonly ISmsQuotaService _quotaService;
        private readonly ICurrentUser _currentUser;

        public SmsLogService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ISmsQuotaService quotaService,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _quotaService = quotaService;
            _currentUser = currentUser;
        }

        public PagedResult<SmsLogDto> GetList(int? taskId, string? phone, byte? status,
            DateTime? startTime, DateTime? endTime, int page, int size)
        {
            var query = _fsql.Select<SmsLogEntity>();
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
            {
                var ownedTaskIds = _fsql.Select<SmsTaskEntity>()
                    .Where(t => t.CreatedById == _currentUser.UserId)
                    .ToList(t => t.Id);
                query = query.Where(l => ownedTaskIds.Contains(l.TaskId));
            }
            if (taskId.HasValue) query = query.Where(l => l.TaskId == taskId.Value);
            if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(l => l.Phone.Contains(phone));
            if (status.HasValue) query = query.Where(l => l.Status == status.Value);
            if (startTime.HasValue) query = query.Where(l => l.CreateTime >= startTime.Value);
            if (endTime.HasValue) query = query.Where(l => l.CreateTime <= endTime.Value);

            var total = query.Count();
            var list = query.OrderByDescending(l => l.CreateTime)
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(l => new SmsLogDto
                {
                    Id = l.Id,
                    TaskId = l.TaskId,
                    Phone = SmsPhoneHelper.Mask(l.Phone),
                    Content = l.Content,
                    ProviderMsgId = l.ProviderMsgId,
                    Status = l.Status,
                    ErrorMessage = l.ErrorMessage,
                    CostMs = l.CostMs,
                    CreateTime = l.CreateTime
                }).ToList();

            return new PagedResult<SmsLogDto> { Total = total, List = list };
        }

        public SmsStatisticsDto GetStatistics() => _quotaService.GetStatistics();

        public SendTrendDto GetTrend(int days)
        {
            // 归一化天数区间（1~90 天），起点为今天往前推 days-1 天的零点
            if (days < 1) days = 1;
            if (days > 90) days = 90;
            var startDate = DateTime.Today.AddDays(-(days - 1));
            var endExclusive = DateTime.Today.AddDays(1);

            var query = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= startDate && l.CreateTime < endExclusive);

            // 数据范围为本人时只统计自己创建的任务下的日志，与 GetList 保持一致
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
            {
                var ownedTaskIds = _fsql.Select<SmsTaskEntity>()
                    .Where(t => t.CreatedById == _currentUser.UserId)
                    .ToList(t => t.Id);
                query = query.Where(l => ownedTaskIds.Contains(l.TaskId));
            }

            // 拉取区间内 (日期, 状态) 明细，在内存按天聚合（Status==1 视为成功）
            var rows = query.ToList(l => new { l.CreateTime, l.Status });
            return TrendBuilder.Build(startDate, days, rows.Select(r => (r.CreateTime, r.Status == 1)));
        }

        public SmsQuotaDto GetQuota() => _quotaService.GetQuotaStatus();
    }
}
