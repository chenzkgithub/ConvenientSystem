namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 按日发送趋势数据点：某一天的成功数、失败数与总数。
    /// 供短信、邮件等模块的首页趋势图共用。
    /// </summary>
    public class DailyTrendPointDto
    {
        /// <summary>日期（yyyy-MM-dd）</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>成功数</summary>
        public int Success { get; set; }

        /// <summary>失败数</summary>
        public int Failed { get; set; }

        /// <summary>总数（成功 + 失败 + 其它状态）</summary>
        public int Total { get; set; }
    }

    /// <summary>
    /// 发送趋势汇总：按日明细序列 + 区间成功/失败/总计合计。
    /// </summary>
    public class SendTrendDto
    {
        /// <summary>按日明细，日期升序，区间内无数据的日期补 0</summary>
        public List<DailyTrendPointDto> Points { get; set; } = new();

        /// <summary>区间总成功数</summary>
        public int TotalSuccess { get; set; }

        /// <summary>区间总失败数</summary>
        public int TotalFailed { get; set; }

        /// <summary>区间总数</summary>
        public int TotalCount { get; set; }
    }
}
