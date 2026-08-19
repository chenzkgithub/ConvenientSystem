using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 机器人发送日志业务服务：分页查询发送日志。
    /// </summary>
    public interface IWebhookLogService
    {
        /// <summary>按条件分页查询机器人发送日志</summary>
        PagedResult<WebhookLogDto> GetList(string? configName, bool? success, int page, int size, string? sortField = null, string? sortOrder = null);
    }
}
