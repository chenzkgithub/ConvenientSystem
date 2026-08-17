using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 审计日志业务服务实现：从配置库分页查询；非管理员只能查看自己的审计日志。
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public AuditLogService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        public PagedResult<AuditLogDto> GetList(string? account, string? module, bool? success,
            DateTime? startTime, DateTime? endTime, int page, int size)
        {
            if (page < 1) page = 1;
            if (size < 1) size = 20;

            var query = _fsql.Select<SysAuditLogEntity>();
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(l => l.UserId == _currentUser.UserId);
            if (!string.IsNullOrWhiteSpace(account)) query = query.Where(l => l.Account.Contains(account));
            if (!string.IsNullOrWhiteSpace(module)) query = query.Where(l => l.Module == module);
            if (success.HasValue) query = query.Where(l => l.Success == success.Value);
            if (startTime.HasValue) query = query.Where(l => l.CreateTime >= startTime.Value);
            if (endTime.HasValue) query = query.Where(l => l.CreateTime <= endTime.Value);

            var total = query.Count();
            var list = query.OrderByDescending(l => l.CreateTime)
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(l => new AuditLogDto
                {
                    Id = l.Id,
                    UserId = l.UserId,
                    Account = l.Account,
                    Action = l.Action,
                    Module = l.Module,
                    Path = l.Path,
                    Method = l.Method,
                    Ip = l.Ip,
                    ParamSummary = l.ParamSummary,
                    Success = l.Success,
                    StatusCode = l.StatusCode,
                    CostMs = l.CostMs,
                    CreateTime = l.CreateTime
                }).ToList();

            return new PagedResult<AuditLogDto> { Total = total, List = list };
        }

        /// <summary>
        /// 按日审计操作趋势：统计近 N 天的成功/失败操作数，带数据权限过滤。
        /// </summary>
        public SendTrendDto GetTrend(int days)
        {
            if (days < 1) days = 1;
            if (days > 90) days = 90;
            var startDate = DateTime.Today.AddDays(-(days - 1));
            var endExclusive = DateTime.Today.AddDays(1);

            var query = _fsql.Select<SysAuditLogEntity>()
                .Where(l => l.CreateTime >= startDate && l.CreateTime < endExclusive);
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(l => l.UserId == _currentUser.UserId);

            var rows = query.ToList(l => new { l.CreateTime, l.Success });
            return TrendBuilder.Build(startDate, days, rows.Select(r => (r.CreateTime, r.Success)));
        }

        /// <summary>
        /// 按日登录活跃趋势：审计中间件会记录登录接口的 POST 请求，
        /// 按登录路径筛选近 N 天的成功/失败登录次数，同样带数据权限过滤。
        /// </summary>
        public SendTrendDto GetLoginTrend(int days)
        {
            if (days < 1) days = 1;
            if (days > 90) days = 90;
            var startDate = DateTime.Today.AddDays(-(days - 1));
            var endExclusive = DateTime.Today.AddDays(1);

            var query = _fsql.Select<SysAuditLogEntity>()
                .Where(l => l.Path.EndsWith("/Login/VerifyLogin"))
                .Where(l => l.CreateTime >= startDate && l.CreateTime < endExclusive);
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(l => l.UserId == _currentUser.UserId);

            var rows = query.ToList(l => new { l.CreateTime, l.Success });
            return TrendBuilder.Build(startDate, days, rows.Select(r => (r.CreateTime, r.Success)));
        }
    }
}
