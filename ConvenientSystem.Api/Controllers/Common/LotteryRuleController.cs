using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 彩票玩法规则接口（奖级对照表数据源、规则版本审核）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("lottery")]
    public class LotteryRuleController : BaseController
    {
        private readonly ILotteryRuleService _ruleService;

        public LotteryRuleController(ILotteryRuleService ruleService)
        {
            _ruleService = ruleService;
        }

        /// <summary>当前判奖依据的规则 + 待审版本（走势图「玩法规则」弹窗用）</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<LotteryRuleViewDto> View([FromQuery] string type = LotteryTypes.DLT)
            => Ok(_ruleService.GetView(type));

        /// <summary>规则版本历史</summary>
        [HttpGet]
        public ActionResult<List<LotteryRuleVersionDto>> Versions([FromQuery] string type = LotteryTypes.DLT)
            => Ok(_ruleService.GetVersions(type));

        /// <summary>审核待审版本（启用或驳回）</summary>
        [HttpPost]
        public ActionResult<bool> Review([FromBody] LotteryRuleReviewDto dto)
            => Ok(_ruleService.Review(dto));

        /// <summary>立即抓取官网规则条文（后台执行，返回 Hangfire 任务 Id）</summary>
        [HttpPost]
        public ActionResult<string> Crawl([FromQuery] string type = LotteryTypes.DLT)
            => Ok(_ruleService.CrawlNow(type));
    }
}
