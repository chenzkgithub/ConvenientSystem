using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信模板业务服务：模板增删改查、启用切换与渲染预览。
    /// </summary>
    public interface ISmsTemplateService
    {
        /// <summary>按分类/关键字查询模板列表</summary>
        List<SmsTemplateDto> GetList(string? category, string? keyword);

        /// <summary>查询单个模板；不存在时抛 NotFoundException</summary>
        SmsTemplateDto Get(int id);

        /// <summary>新建模板，返回落库后的模板</summary>
        SmsTemplateDto Create(SmsTemplateDto dto);

        /// <summary>更新模板；不存在时抛 NotFoundException</summary>
        void Update(SmsTemplateDto dto);

        /// <summary>删除模板</summary>
        void Delete(int id);

        /// <summary>切换启用状态，返回切换后的状态；不存在时抛 NotFoundException</summary>
        ToggleEnabledDto ToggleEnabled(int id);

        /// <summary>按变量字典渲染模板内容</summary>
        TemplatePreviewDto Preview(PreviewTemplateRequest req);

        /// <summary>提取模板内容中的变量名</summary>
        List<string> ExtractVariables(string content);
    }
}
