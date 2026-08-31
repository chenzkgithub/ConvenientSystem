using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 开发工具集接口：雪花ID生成等开发者常用工具。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("dev-tools")]
    public class DevToolsController : BaseController
    {
        private readonly ISnowflakeIdService _snowflake;

        public DevToolsController(ISnowflakeIdService snowflake)
        {
            _snowflake = snowflake;
        }

        /// <summary>
        /// 生成雪花ID（Snowflake ID）。
        /// count 为生成数量（1～1000），默认 1。
        /// epoch 为可选的起始纪元日期（如 2020-01-01），传入后以该日期为基准生成ID，
        /// ID 位数由所选日期决定，不做强制限制。
        /// 返回 { ids: string[] }，以字符串形式返回避免 JS 精度丢失。
        /// </summary>
        [HttpGet]
        public ActionResult<object> SnowflakeId([FromQuery] int count = 1, [FromQuery] DateTime? epoch = null)
        {
            var ids = _snowflake.NextIds(count, epoch);
            // 64 位长整型超过 JS Number.MAX_SAFE_INTEGER，以字符串返回避免精度丢失
            return Ok(new
            {
                ids = ids.Select(id => id.ToString()).ToArray(),
                count = ids.Length,
            });
        }
    }
}
