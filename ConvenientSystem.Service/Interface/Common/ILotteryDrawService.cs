using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票开奖记录服务：管理多彩种开奖数据、提供走势分析。
    /// </summary>
    public interface ILotteryDrawService
    {
        /// <summary>彩种配置（选号分区）</summary>
        LotteryConfigDto GetConfig(string type);

        /// <summary>分页查询开奖记录（按期号倒序）</summary>
        PagedResult<LotteryDrawDto> GetDraws(string type, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>批量导入开奖记录</summary>
        int ImportDraws(string type, List<LotteryDrawItem> draws);

        /// <summary>删除指定开奖记录</summary>
        bool DeleteDraw(int id);

        /// <summary>
        /// 获取走势图分析数据：指定日期区间时按开奖日期筛选，否则取最近 N 期。
        /// 传入 matchFront/matchBack/matchPos 时转为历史号码匹配模式：忽略期数与日期区间，
        /// 在全库内检索同时满足全部条件的期（选几个号码就要几个全开出），按期号降序返回。
        /// matchPos 为位置型彩种的数位条件：键为数位序号，值为该位候选数字（指定的每一位都要对上，各位可要求同一数字）。
        /// </summary>
        LotteryTrendDto GetTrend(string type, int periods, DateTime? startDate = null, DateTime? endDate = null,
            int[]? matchFront = null, int[]? matchBack = null, Dictionary<int, int[]>? matchPos = null);

        /// <summary>查询指定开奖期的官网通告数据（全国中奖明细/销量/奖池），期号不存在时报 404</summary>
        LotteryDrawNoticeDto GetDrawNotice(string type, string issue);
    }
}
