namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 雪花ID（Snowflake）生成服务：生成全局唯一、趋势递增的 64 位长整型 ID。
    /// </summary>
    public interface ISnowflakeIdService
    {
        /// <summary>生成单个雪花ID</summary>
        long NextId();

        /// <summary>批量生成雪花ID（count 会被限制在 1～1000）</summary>
        long[] NextIds(int count);

        /// <summary>
        /// 批量生成雪花ID，可指定起始纪元日期（epoch）。
        /// 当 epoch 有值时以该日期 UTC 零点为纪元，ID 位数由所选日期决定；
        /// epoch 为 null 时使用默认纪元（Twitter Twepoch：2010-11-04）。
        /// </summary>
        long[] NextIds(int count, DateTime? epoch);
    }
}
