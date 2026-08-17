using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 开奖结果每日汇总服务：按日期聚合当天开奖彩种、全国中奖情况及全部用户选号中奖结果。
    /// </summary>
    public interface ILotterySummaryService
    {
        /// <summary>
        /// 获取指定日期的开奖汇总。当天无开奖时自动回退为各彩种最新一期。
        /// </summary>
        /// <param name="date">汇总日期，null 时取当天</param>
        /// <param name="ct">取消令牌</param>
        Task<LotteryResultSummaryDto> GetSummaryAsync(DateTime? date = null, CancellationToken ct = default);
    }
}
