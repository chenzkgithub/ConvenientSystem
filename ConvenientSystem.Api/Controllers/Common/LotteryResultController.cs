using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 开奖结果汇总：供群机器人卡片详情页等外部入口只读访问。
    /// </summary>
    [Area("Common")]
    [Route("api/[area]/[controller]/[action]")]
    [ApiController]
    [AllowAnonymous]
    public class LotteryResultController : ControllerBase
    {
        private readonly ILotterySummaryService _summaryService;

        public LotteryResultController(ILotterySummaryService summaryService)
        {
            _summaryService = summaryService;
        }

        /// <summary>
        /// 获取指定日期的开奖结果汇总；未指定日期时取当天（无开奖则回退最新一期）。
        /// </summary>
        [HttpGet]
        public async Task<LotteryResultSummaryDto> GetSummary([FromQuery] DateTime? date, CancellationToken ct)
        {
            return await _summaryService.GetSummaryAsync(date, ct);
        }
    }
}
