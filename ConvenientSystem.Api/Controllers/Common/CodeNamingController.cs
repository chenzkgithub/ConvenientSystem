using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 代码命名转换接口：中文翻译为英文并生成多种命名规范
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("code-naming")]
    public class CodeNamingController : BaseController
    {
        private readonly ICodeNamingService _namingService;

        public CodeNamingController(ICodeNamingService namingService)
        {
            _namingService = namingService;
        }

        /// <summary>将中文翻译为英文单词列表（前端据此生成驼峰/下划线等命名）</summary>
        [HttpGet]
        public ActionResult<CodeNamingTranslateDto> Translate([FromQuery] string text)
            => Ok(_namingService.Translate(text));
    }
}
