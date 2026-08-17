using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 按日发送趋势聚合器：把 (时间, 是否成功) 明细在内存按天分组，
    /// 补齐区间内无数据的日期为 0，并计算区间合计。短信/邮件共用。
    /// </summary>
    public static class TrendBuilder
    {
        /// <summary>
        /// 聚合按日趋势。
        /// </summary>
        /// <param name="startDate">起始日期（零点，含）</param>
        /// <param name="days">天数（含起始日往后连续 days 天）</param>
        /// <param name="rows">明细序列：(创建时间, 是否成功)</param>
        public static SendTrendDto Build(DateTime startDate, int days, IEnumerable<(DateTime time, bool success)> rows)
        {
            // 预置每一天的空桶，保证区间内日期连续、无数据补 0
            var buckets = new Dictionary<string, DailyTrendPointDto>();
            var ordered = new List<DailyTrendPointDto>(days);
            for (var i = 0; i < days; i++)
            {
                var key = startDate.AddDays(i).ToString("yyyy-MM-dd");
                var point = new DailyTrendPointDto { Date = key };
                buckets[key] = point;
                ordered.Add(point);
            }

            foreach (var (time, success) in rows)
            {
                var key = time.ToString("yyyy-MM-dd");
                if (!buckets.TryGetValue(key, out var point)) continue; // 落在区间外的忽略
                point.Total++;
                if (success) point.Success++;
                else point.Failed++;
            }

            return new SendTrendDto
            {
                Points = ordered,
                TotalSuccess = ordered.Sum(p => p.Success),
                TotalFailed = ordered.Sum(p => p.Failed),
                TotalCount = ordered.Sum(p => p.Total),
            };
        }
    }
}
