using ConvenientSystem.Shared.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 雪花ID生成服务实现：基于 Shared 层 SnowflakeId 公共类（标准 Twitter 算法）。
    /// 默认纪元为 Twitter Twepoch（2010-11-04），5 位数据中心 + 5 位机器 + 12 位序列号。
    /// 支持自定义纪元日期：通过继承 SnowflakeId 覆盖 TimeGen 偏移时间戳实现。
    /// </summary>
    public class SnowflakeIdService : ISnowflakeIdService
    {
        /// <inheritdoc />
        public long NextId()
        {
            return SnowflakeId.Default().NextId();
        }

        /// <inheritdoc />
        public long[] NextIds(int count)
        {
            var n = Math.Clamp(count, 1, 1000);
            var result = new long[n];
            var sf = SnowflakeId.Default();
            for (var i = 0; i < n; i++)
            {
                result[i] = sf.NextId();
            }
            return result;
        }

        /// <inheritdoc />
        public long[] NextIds(int count, DateTime? epoch)
        {
            // epoch 为 null 时走默认纪元 + 单例
            if (!epoch.HasValue)
            {
                return NextIds(count);
            }

            // 用户指定日期：取该日期 UTC 零点的毫秒时间戳作为纪元，
            // 通过覆盖 TimeGen 偏移时间戳，使生成的 ID 以所选日期为基准。
            var dto = new DateTimeOffset(epoch.Value.Date, TimeSpan.Zero);
            var epochMillis = dto.ToUnixTimeMilliseconds();

            var n = Math.Clamp(count, 1, 1000);
            var result = new long[n];
            var sf = new EpochSnowflakeId(epochMillis);
            for (var i = 0; i < n; i++)
            {
                result[i] = sf.NextId();
            }
            return result;
        }

        /// <summary>
        /// 带自定义纪元的雪花ID生成器：覆盖 TimeGen 返回偏移后的时间戳，
        /// 使 NextId 中的 (timestamp - Twepoch) 等价于 (实际时间戳 - 自定义纪元)。
        /// offset = Twepoch - epochMillis，TimeGen 返回 实际时间戳 + offset。
        /// </summary>
        private class EpochSnowflakeId : SnowflakeId
        {
            private readonly long _offset;

            public EpochSnowflakeId(long epochMillis) : base(1, 0)
            {
                _offset = SnowflakeId.Twepoch - epochMillis;
            }

            protected override long TimeGen()
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _offset;
            }
        }
    }
}
