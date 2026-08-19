using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票选号记录服务：按用户、按彩种存取选号记录。
    /// </summary>
    public interface ILotteryService
    {
        /// <summary>分页查询当前用户指定彩种的选号记录（可选按日期过滤），按时间倒序</summary>
        PagedResult<LotteryBetDto> GetRecords(string type, string? date, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>批量保存选号记录</summary>
        List<LotteryBetDto> SaveBets(string type, List<LotteryBetItem> bets);

        /// <summary>删除指定记录（仅限本人）</summary>
        bool DeleteRecord(int id);

        /// <summary>删除指定彩种、指定日期的全部记录（仅限本人）</summary>
        int DeleteByDate(string type, string date);

        /// <summary>首页展示：各彩种最新一期开奖号码 + 当前用户开奖当日选号的逐注中奖结果</summary>
        List<LotteryHomeResultDto> GetHomeResults();

        /// <summary>验证指定选号记录（仅限本人）：对应开奖期的奖级判定 + 官网通告中奖明细</summary>
        LotteryVerifyDto VerifyBet(int id);

        /// <summary>
        /// 批量验证整期选号（仅限本人）：date 指定时取该开奖日那一期，为空时取最新一期
        /// </summary>
        LotteryIssueVerifyDto VerifyIssue(string type, DateTime? date);
    }
}
