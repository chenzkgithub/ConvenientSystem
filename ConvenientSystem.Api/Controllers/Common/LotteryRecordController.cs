using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 选号记录查询与中奖验证接口（"选号记录"菜单专用，多彩种共用）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("lottery-records")]
    public class LotteryRecordController : BaseController
    {
        private readonly ILotteryService _lotteryService;

        public LotteryRecordController(ILotteryService lotteryService)
        {
            _lotteryService = lotteryService;
        }

        /// <summary>分页查询当前用户选号记录（可选按日期过滤，格式 yyyy-MM-dd）</summary>
        [HttpGet]
        public ActionResult<PagedResult<LotteryBetDto>> List(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] string? date = null,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortOrder = null)
            => Ok(_lotteryService.GetRecords(type, date, page, size, sortField, sortOrder));

        /// <summary>验证指定选号记录：对应开奖期的奖级判定 + 官网通告中奖明细（全国注数/单注奖金）</summary>
        [HttpGet]
        [PermissionAuthorize("lottery-records:verify")]
        public ActionResult<LotteryVerifyDto> Verify([FromQuery] int id)
            => Ok(_lotteryService.VerifyBet(id));

        /// <summary>
        /// 批量验证整期选号：date（yyyy-MM-dd）指定时取该开奖日那一期，为空时取最新一期。
        /// 开奖号码与官网通告只返回一份，逐注结果在 Bets 内
        /// </summary>
        [HttpGet]
        [PermissionAuthorize("lottery-records:verify-issue")]
        public ActionResult<LotteryIssueVerifyDto> VerifyIssue(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] string? date = null)
        {
            DateTime? day = DateTime.TryParse(date, out var parsed) ? parsed : null;
            return Ok(_lotteryService.VerifyIssue(type, day));
        }
    }
}
