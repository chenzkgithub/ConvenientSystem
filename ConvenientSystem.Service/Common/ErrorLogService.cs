using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 错误日志业务服务实现：从配置库分页查询；非管理员只能查看自己的错误日志。
    /// </summary>
    public class ErrorLogService : IErrorLogService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public ErrorLogService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        public PagedResult<ErrorLogDto> GetList(string? keyword, DateTime? startTime, DateTime? endTime, int page, int size)
        {
            if (page < 1) page = 1;
            if (size < 1) size = 20;

            var query = _fsql.Select<SysErrorLogEntity>();
            if (_currentUser.DataScope != DataScope.All && _currentUser.UserId.HasValue)
                query = query.Where(l => l.UserId == _currentUser.UserId);
            if (startTime.HasValue) query = query.Where(l => l.CreateTime >= startTime.Value);
            if (endTime.HasValue) query = query.Where(l => l.CreateTime <= endTime.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(l => l.ErrorMessage.Contains(keyword) || l.Path.Contains(keyword) || l.ExceptionType.Contains(keyword));

            var total = query.Count();
            var list = query.OrderByDescending(l => l.CreateTime)
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(l => new ErrorLogDto
                {
                    Id = l.Id,
                    Account = l.Account,
                    Path = l.Path,
                    Method = l.Method,
                    StatusCode = l.StatusCode,
                    ExceptionType = l.ExceptionType,
                    ErrorMessage = l.ErrorMessage,
                    StackTrace = l.StackTrace,
                    Ip = l.Ip,
                    CreateTime = l.CreateTime
                }).ToList();

            return new PagedResult<ErrorLogDto> { Total = total, List = list };
        }

        public int Clear()
        {
            return _fsql.Delete<SysErrorLogEntity>().Where("1=1").ExecuteAffrows();
        }
    }
}
