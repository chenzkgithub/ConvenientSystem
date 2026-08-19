using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 机器人发送日志业务服务实现：从配置库分页查询。
    /// </summary>
    public class WebhookLogService : IWebhookLogService
    {
        private readonly IFreeSql _fsql;

        public WebhookLogService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public PagedResult<WebhookLogDto> GetList(string? configName, bool? success, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            if (page < 1) page = 1;
            if (size < 1) size = 20;

            var query = _fsql.Select<SysWebhookLogEntity>();
            if (!string.IsNullOrWhiteSpace(configName))
                query = query.Where(l => l.ConfigName.Contains(configName));
            if (success.HasValue)
                query = query.Where(l => l.Success == success.Value);

            var total = query.Count();
            var sortedQuery = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(l => l.CreateTime) : query.OrderByDynamic(sortField, sortOrder);
            var list = sortedQuery
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(l => new WebhookLogDto
                {
                    Id = l.Id,
                    ConfigId = l.ConfigId,
                    ConfigName = l.ConfigName,
                    ProviderType = l.ProviderType,
                    Title = l.Title,
                    Content = l.Content,
                    Success = l.Success,
                    ErrorMessage = l.ErrorMessage,
                    CostMs = l.CostMs,
                    CreateTime = l.CreateTime
                }).ToList();

            return new PagedResult<WebhookLogDto> { Total = total, List = list };
        }
    }
}
