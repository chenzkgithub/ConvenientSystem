using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 彩票开奖记录与走势图接口（多彩种：DLT/SSQ/PL5/FC3D）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("lottery")]
    public class LotteryDrawController : BaseController
    {
        private readonly ILotteryDrawService _drawService;

        public LotteryDrawController(ILotteryDrawService drawService)
        {
            _drawService = drawService;
        }

        /// <summary>彩种配置（名称与选号分区，选号页渲染用）</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<LotteryConfigDto> Config([FromQuery] string type = LotteryTypes.DLT)
            => Ok(_drawService.GetConfig(type));

        /// <summary>分页查询开奖记录</summary>
        [HttpGet]
        public ActionResult<PagedResult<LotteryDrawDto>> List(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
            => Ok(_drawService.GetDraws(type, page, size));

        /// <summary>批量导入开奖记录</summary>
        [HttpPost]
        public ActionResult<int> Import([FromBody] LotteryDrawImportRequest request)
            => Ok(_drawService.ImportDraws(request.Type, request.Draws));

        /// <summary>删除单条开奖记录</summary>
        [HttpDelete]
        public ActionResult<bool> Delete([FromQuery] int id)
            => Ok(_drawService.DeleteDraw(id));

        /// <summary>
        /// 获取走势图分析数据：指定日期区间时按开奖日期筛选，否则取最近 N 期（默认 50）。
        /// matchFront/matchBack 为逗号分隔的号码串，matchPos 为数位条件串，任一非空时转为历史号码匹配模式。
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<LotteryTrendDto> Trend(
            [FromQuery] string type = LotteryTypes.DLT,
            [FromQuery] int periods = 50,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? matchFront = null,
            [FromQuery] string? matchBack = null,
            [FromQuery] string? matchPos = null)
            => Ok(_drawService.GetTrend(type, periods, startDate, endDate,
                ParseNums(matchFront), ParseNums(matchBack), ParsePos(matchPos)));

        /// <summary>解析逗号分隔的号码串为号码数组，非数字与负数项忽略（福彩3D/排列五包含 0，故不排除 0）</summary>
        private static int[] ParseNums(string? text)
            => string.IsNullOrWhiteSpace(text)
                ? Array.Empty<int>()
                : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : -1)
                    .Where(n => n >= 0)
                    .Distinct()
                    .ToArray();

        /// <summary>
        /// 解析数位条件串：形如 0:57,4:9，冒号前为数位序号（万位起0），后为该位候选数字。
        /// 位置型彩种各位均为单数字 0-9，故候选数字直接连写无需分隔符。
        /// </summary>
        private static Dictionary<int, int[]>? ParsePos(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var dict = new Dictionary<int, int[]>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[0], out var idx) || idx < 0) continue;
                var digits = kv[1].Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();
                // 候选为空等于该位不限，直接不入字典
                if (digits.Length > 0) dict[idx] = digits;
            }
            return dict.Count > 0 ? dict : null;
        }

        /// <summary>指定开奖期的官网通告数据（全国中奖明细/销量/奖池，走势图双击查看用）</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<LotteryDrawNoticeDto> Notice([FromQuery] string type = LotteryTypes.DLT, [FromQuery] string issue = "")
            => Ok(_drawService.GetDrawNotice(type, issue));
    }
}
