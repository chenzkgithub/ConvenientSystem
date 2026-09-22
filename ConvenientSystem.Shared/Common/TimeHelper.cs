namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 系统统一时间源：始终返回中国时区（UTC+8）的当前墙钟时间，与服务器操作系统时区无关。
    /// 背景：部署服务器（Linux/Docker）时区为 UTC，直接使用 DateTime.Now / DateTime.UtcNow
    /// 会让前端按字符串截取展示的时间慢 8 小时（在线用户登录时间、AI 会话、聊天、审计日志等）。
    /// 因此系统内所有"存储 / 展示 / 与库中时间比较"的场景必须统一使用 TimeHelper.Now，
    /// 禁止再引入 DateTime.Now / DateTime.UtcNow（仅纯内存相对耗时计算且两端同源的除外）。
    /// </summary>
    public static class TimeHelper
    {
        private static readonly TimeZoneInfo ChinaZone = ResolveChinaZone();

        /// <summary>当前中国时间（墙钟值；序列化不带 Z 后缀，前端 formatDate 截取即正确显示）。</summary>
        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaZone);

        /// <summary>
        /// 把带时区信息的时间（如前端 toISOString 传来的 UTC 时间）转换为中国墙钟时间；
        /// 无 Kind 信息（Unspecified）时视为已是中国时间，原样返回。
        /// </summary>
        public static DateTime FromUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Unspecified) return value;
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, ChinaZone);
        }

        private static TimeZoneInfo ResolveChinaZone()
        {
            // Windows 时区 ID 与 Linux/IANA ID 都尝试，最后兜底用固定 +8 自定义时区
            try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
            catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
            catch { }
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "中国标准时间", "中国标准时间");
        }
    }
}
