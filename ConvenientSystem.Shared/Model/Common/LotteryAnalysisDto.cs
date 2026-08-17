namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 彩票智能分析结果：基于历史数据的多维度评分 + 推荐号码 + AI 组合
    /// </summary>
    public class LotteryAnalysisDto
    {
        /// <summary>彩种代码</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>彩种名称</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>分析期数</summary>
        public int Periods { get; set; }

        /// <summary>下一期期号</summary>
        public string? NextIssue { get; set; }

        /// <summary>下一期开奖日期</summary>
        public DateTime? NextDrawDate { get; set; }

        /// <summary>前区每个号码的评分详情</summary>
        public List<NumberScoreDto> FrontScores { get; set; } = new();

        /// <summary>后区每个号码的评分详情（位置型彩种为空）</summary>
        public List<NumberScoreDto> BackScores { get; set; } = new();

        /// <summary>推荐前区号码（按评分降序，个数与选号规则一致）</summary>
        public int[] RecommendedFront { get; set; } = Array.Empty<int>();

        /// <summary>推荐后区号码</summary>
        public int[] RecommendedBack { get; set; } = Array.Empty<int>();

        /// <summary>热号池（前区近期最热的号码，按频率降序取 10 个）</summary>
        public int[] HotNumbers { get; set; } = Array.Empty<int>();

        /// <summary>冷号池（前区遗漏最严重的号码，按 CurrentMiss 降序取 10 个）</summary>
        public int[] ColdNumbers { get; set; } = Array.Empty<int>();

        /// <summary>AI 组合（3~5 注完整号码，基于精选号码按区间均衡生成）</summary>
        public List<LotteryBetItem> GeneratedBets { get; set; } = new();

        /// <summary>分析摘要文字</summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单个号码的多维度评分
    /// </summary>
    public class NumberScoreDto
    {
        /// <summary>号码</summary>
        public int Number { get; set; }

        /// <summary>综合评分（0~100）</summary>
        public double Score { get; set; }

        /// <summary>热号分（近期出现频率，0~100）</summary>
        public double HotScore { get; set; }

        /// <summary>冷号回补分（CurrentMiss / AvgMiss 越高分越高，0~100）</summary>
        public double ColdScore { get; set; }

        /// <summary>遗漏极值分（CurrentMiss / MaxMiss 越接近 1 分越高，0~100）</summary>
        public double MissScore { get; set; }

        /// <summary>连号趋势分（近 10 期与相邻号码同出频率，0~100）</summary>
        public double ConsecutiveScore { get; set; }

        /// <summary>区间均衡分（所在区间偏冷程度，0~100）</summary>
        public double ZoneScore { get; set; }

        /// <summary>当前遗漏</summary>
        public int CurrentMiss { get; set; }

        /// <summary>平均遗漏</summary>
        public double AvgMiss { get; set; }

        /// <summary>最大遗漏</summary>
        public int MaxMiss { get; set; }

        /// <summary>出现次数</summary>
        public int Count { get; set; }

        /// <summary>所属分区标签（位置型彩种：万位/千位/…；池选型：前区/后区）</summary>
        public string ZoneLabel { get; set; } = string.Empty;
    }
}
