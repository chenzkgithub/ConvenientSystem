using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 彩票智能分析接口：基于历史数据的多维度评分与号码推荐
    /// </summary>
    [Area("Common")]
    public class LotteryAnalysisController : BaseController
    {
        private readonly ILotteryAnalysisService _analysisService;

        public LotteryAnalysisController(ILotteryAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        /// <summary>生成智能分析报告：对每个号码 5 维评分，输出推荐号码、热/冷号池、AI 组合与摘要</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<LotteryAnalysisDto> Predict(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] int periods = 100)
            => Ok(_analysisService.Predict(type, periods));
    }
}
