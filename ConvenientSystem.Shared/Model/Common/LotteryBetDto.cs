namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 大乐透选号记录 DTO
    /// </summary>
    public class LotteryBetDto
    {
        /// <summary>记录 Id</summary>
        public int Id { get; set; }

        /// <summary>前区号码（已排序）</summary>
        public int[] Front { get; set; } = System.Array.Empty<int>();

        /// <summary>后区号码（已排序）</summary>
        public int[] Back { get; set; } = System.Array.Empty<int>();

        /// <summary>所属期号（保存时默认取下一期；历史记录为 null）</summary>
        public string? IssueNumber { get; set; }

        /// <summary>开奖日期（保存时默认取下一期开奖日；历史记录为 null）</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>选号时间（ISO 8601）</summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 批量保存选号请求
    /// </summary>
    public class LotterySaveRequest
    {
        /// <summary>彩种代码（默认 DLT）</summary>
        public string Type { get; set; } = LotteryTypes.DLT;

        /// <summary>要保存的注数列表</summary>
        public List<LotteryBetItem> Bets { get; set; } = new();
    }

    /// <summary>
    /// 单注号码
    /// </summary>
    public class LotteryBetItem
    {
        /// <summary>前区号码</summary>
        public int[] Front { get; set; } = System.Array.Empty<int>();

        /// <summary>后区号码</summary>
        public int[] Back { get; set; } = System.Array.Empty<int>();
    }
}
