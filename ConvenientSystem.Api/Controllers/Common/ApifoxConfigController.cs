using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// Apifox 导入与当前用户 Access Token 维护接口。
    /// Access Token 按当前登录用户加密保存到 UserConfig；项目及导入选项只在导入请求中使用。
    /// </summary>
    [Area("Common")]
    public class ApifoxConfigController : BaseController
    {
        private readonly IApifoxImportService _service;

        public ApifoxConfigController(IApifoxImportService service)
        {
            _service = service;
        }

        /// <summary>读取当前用户的 Apifox Access Token 保存状态，不返回令牌内容。</summary>
        [HttpGet]
        public ActionResult<ApifoxAccessTokenStatusDto> GetMyAccessTokenStatus()
            => Ok(_service.GetMyAccessTokenStatus());

        /// <summary>保存或清除当前用户的 Apifox Access Token，令牌会在服务端保护后写入 UserConfig。</summary>
        [HttpPut]
        public IActionResult SaveMyAccessToken([FromBody] ApifoxAccessTokenSaveRequest request)
        {
            _service.SaveMyAccessToken(request ?? new ApifoxAccessTokenSaveRequest());
            return Ok(new { message = "Apifox Access Token 已保存" });
        }

        /// <summary>导入 OpenAPI 3 JSON 到本次指定的 Apifox 项目。</summary>
        [HttpPost]
        [PermissionAuthorize("api-spec")]
        public async Task<ActionResult<ApifoxImportResultDto>> ImportOpenApi(
            [FromBody] ApifoxImportRequest request,
            CancellationToken cancellationToken)
            => Ok(await _service.ImportAsync(request ?? new ApifoxImportRequest(), cancellationToken));
    }
}
