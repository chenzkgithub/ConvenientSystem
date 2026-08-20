using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Sms;
using ConvenientSystem.Service.Sms;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Sms
{
    /// <summary>
    /// 短信模板管理接口
    /// </summary>
    [Area("Sms")]
    [PermissionAuthorize("sms-template")]
    public class SmsTemplateController : BaseController
    {
        private readonly ISmsTemplateService _templateService;

        public SmsTemplateController(ISmsTemplateService templateService)
        {
            _templateService = templateService;
        }

        /// <summary>查询全部模板</summary>
        [HttpGet]
        public ActionResult<List<SmsTemplateDto>> List([FromQuery] string? category, [FromQuery] string? keyword)
            => Ok(_templateService.GetList(category, keyword));

        /// <summary>查询单个模板</summary>
        [HttpGet]
        public ActionResult<SmsTemplateDto> Get([FromQuery] int id)
            => Ok(_templateService.Get(id));

        /// <summary>新建模板</summary>
        [HttpPost]
        [PermissionAuthorize("sms-template:create")]
        public ActionResult<SmsTemplateDto> Create([FromBody] SmsTemplateDto dto)
            => Ok(_templateService.Create(dto));

        /// <summary>更新模板</summary>
        [HttpPost]
        [PermissionAuthorize("sms-template:edit")]
        public IActionResult Update([FromBody] SmsTemplateDto dto)
        {
            _templateService.Update(dto);
            return Ok();
        }

        /// <summary>删除模板</summary>
        [HttpPost]
        [PermissionAuthorize("sms-template:delete")]
        public IActionResult Delete([FromQuery] int id)
        {
            _templateService.Delete(id);
            return Ok();
        }

        /// <summary>切换启用状态</summary>
        [HttpPost]
        [PermissionAuthorize("sms-template:toggle")]
        public ActionResult<ToggleEnabledDto> ToggleEnabled([FromQuery] int id)
            => Ok(_templateService.ToggleEnabled(id));

        /// <summary>预览模板渲染效果</summary>
        [HttpPost]
        public ActionResult<TemplatePreviewDto> Preview([FromBody] PreviewTemplateRequest req)
            => Ok(_templateService.Preview(req));

        /// <summary>提取模板中的变量名</summary>
        [HttpPost]
        public ActionResult<List<string>> ExtractVariables([FromQuery] string content)
            => Ok(_templateService.ExtractVariables(content));
    }
}
