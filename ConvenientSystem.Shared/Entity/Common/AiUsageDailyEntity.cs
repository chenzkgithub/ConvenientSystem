using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// AI 每日用量（每用户每天一行）：请求次数与 token 数双配额，发送前检查、完成后累加；
    /// (UtcDate, UserId) 唯一约束，Upsert 幂等累加。
    /// </summary>
    [Table(Name = "AiUsageDaily")]
    public class AiUsageDailyEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>UTC 日期（零点；与 UserId 联合唯一）</summary>
        public DateTime UtcDate { get; set; }

        /// <summary>归属用户（Guid 字符串）</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>今日请求次数（每次 Send +1）</summary>
        public int RequestCount { get; set; }

        /// <summary>今日输入 token 累计</summary>
        public int TokensIn { get; set; }

        /// <summary>今日输出 token 累计</summary>
        public int TokensOut { get; set; }

        public DateTime UpdateTime { get; set; } = TimeHelper.Now;
    }
}
