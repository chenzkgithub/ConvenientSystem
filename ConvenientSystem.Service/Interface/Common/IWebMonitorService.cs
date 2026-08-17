using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 网站/API 监控业务服务：监控目标增删改查、探测日志查询与手动立即检测。
    /// 定时巡检与状态告警由 WebMonitorCheckJob 负责，本服务仅管理配置与查询。
    /// </summary>
    public interface IWebMonitorService
    {
        /// <summary>查询全部监控目标（按创建时间倒序）</summary>
        List<WebMonitorTargetDto> List();

        /// <summary>新增或编辑监控目标，返回 Id</summary>
        int Save(WebMonitorTargetSaveDto dto);

        /// <summary>删除监控目标及其探测日志</summary>
        void Delete(int id);

        /// <summary>分页查询指定目标的探测日志（时间倒序）</summary>
        PagedResult<WebMonitorLogDto> GetLogs(int targetId, int page, int size);

        /// <summary>立即对指定目标执行一次探测（状态变化同样触发邮件告警），返回本次探测结果</summary>
        Task<WebMonitorLogDto> CheckNow(int id);

        /// <summary>监控健康度汇总（首页数据看板用）：按最近探测状态计数 + 异常目标明细</summary>
        MonitorHealthDto GetHealth();
    }
}
