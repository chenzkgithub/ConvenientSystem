using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票玩法规则服务：维护自官网抓取的规则版本，供判奖取用与页面展示奖级对照表。
    /// 规则变更一律先入待审队列，人工确认后才切为生效版本。
    /// </summary>
    public interface ILotteryRuleService
    {
        /// <summary>玩法规则弹窗数据：当前生效版本（无则内置兜底）+ 待审版本</summary>
        LotteryRuleViewDto GetView(string type);

        /// <summary>规则版本历史（按版本倒序）</summary>
        List<LotteryRuleVersionDto> GetVersions(string type);

        /// <summary>审核待审版本：启用则切为生效并清判奖缓存，驳回则标记已驳回</summary>
        bool Review(LotteryRuleReviewDto dto);

        /// <summary>立即抓取指定彩种官网规则（后台任务），返回 Hangfire 任务 Id</summary>
        string CrawlNow(string type);
    }
}
