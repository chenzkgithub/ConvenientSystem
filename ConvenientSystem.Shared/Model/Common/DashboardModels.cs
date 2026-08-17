namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>监控健康度汇总（首页数据看板用）：按最近探测状态计数 + 异常目标明细</summary>
    public class MonitorHealthDto
    {
        /// <summary>监控目标总数</summary>
        public int Total { get; set; }

        /// <summary>启用中的目标数</summary>
        public int EnabledCount { get; set; }

        /// <summary>最近探测正常数</summary>
        public int OkCount { get; set; }

        /// <summary>最近探测异常数</summary>
        public int FailCount { get; set; }

        /// <summary>尚未探测过的目标数</summary>
        public int PendingCount { get; set; }

        /// <summary>异常目标明细（按最近探测时间升序）</summary>
        public List<MonitorFailedItemDto> FailedTargets { get; set; } = new();
    }

    /// <summary>异常监控目标明细</summary>
    public class MonitorFailedItemDto
    {
        public int Id { get; set; }

        /// <summary>监控目标名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>最近异常原因</summary>
        public string? ErrorMsg { get; set; }

        /// <summary>最近探测时间</summary>
        public DateTime? LastCheckAt { get; set; }
    }
}
