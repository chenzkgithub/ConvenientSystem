using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 彩票选号记录接口（多彩种：DLT/SSQ/PL5/FC3D）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("lottery")]
    public class LotteryController : BaseController
    {
        private readonly ILotteryService _lotteryService;

        public LotteryController(ILotteryService lotteryService)
        {
            _lotteryService = lotteryService;
        }

        /// <summary>分页查询选号记录（可选按日期过滤，格式 yyyy-MM-dd）</summary>
        [HttpGet]
        public ActionResult<PagedResult<LotteryBetDto>> List(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] string? date = null,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortOrder = null)
            => Ok(_lotteryService.GetRecords(type, date, page, size, sortField, sortOrder));

        /// <summary>批量保存选号记录（彩种代码在请求体 Type 字段）</summary>
        [HttpPost]
        [PermissionAuthorize("lottery:save-bets", "lottery-ssq:save-bets", "lottery-pl5:save-bets", "lottery-fc3d:save-bets")]
        public ActionResult<List<LotteryBetDto>> Save([FromBody] LotterySaveRequest request)
            => Ok(_lotteryService.SaveBets(request.Type, request.Bets));

        /// <summary>删除单条记录</summary>
        [HttpDelete]
        [PermissionAuthorize("lottery:delete-record", "lottery-ssq:delete-record", "lottery-pl5:delete-record", "lottery-fc3d:delete-record")]
        public ActionResult<bool> Delete([FromQuery] int id)
            => Ok(_lotteryService.DeleteRecord(id));

        /// <summary>删除指定彩种、指定日期的全部记录（格式 yyyy-MM-dd）</summary>
        [HttpDelete]
        [PermissionAuthorize("lottery:clear-history", "lottery-ssq:clear-history", "lottery-pl5:clear-history", "lottery-fc3d:clear-history")]
        public ActionResult<int> DeleteByDate([FromQuery] string type = LotteryTypes.DLT, [FromQuery] string date = "")
            => Ok(_lotteryService.DeleteByDate(type, date));

        /// <summary>各彩种最新开奖与当前用户中奖结果（首页展示用）</summary>
        [HttpGet]
        public ActionResult<List<LotteryHomeResultDto>> HomeResults()
            => Ok(_lotteryService.GetHomeResults());
    }
}
