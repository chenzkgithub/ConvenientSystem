using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 错误日志业务服务：分页查询系统未处理异常记录。
    /// </summary>
    public interface IErrorLogService
    {
        /// <summary>分页查询错误日志（按时间倒序）。</summary>
        PagedResult<ErrorLogDto> GetList(string? keyword, DateTime? startTime, DateTime? endTime, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>清空全部错误日志。</summary>
        int Clear();
    }
}
