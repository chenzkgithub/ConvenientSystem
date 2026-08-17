using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统运行状态大盘服务接口
    /// </summary>
    public interface ISystemDashboardService
    {
        SystemDashboardDto GetDashboard();
    }
}
