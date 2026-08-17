using ConvenientSystem.Shared.Model.Email;

namespace ConvenientSystem.Service.Email
{
    /// <summary>
    /// 邮件配置业务服务：SMTP 配置列表 CRUD 与测试发送。
    /// </summary>
    public interface IEmailConfigService
    {
        /// <summary>获取全部邮件配置列表（默认排前，密码字段脱敏）</summary>
        List<EmailConfigDto> GetConfigs();

        /// <summary>新增或更新邮件配置（Id<=0 新增，否则更新）；密码为占位符时保留原密码</summary>
        void Save(EmailConfigDto dto);

        /// <summary>删除邮件配置</summary>
        void Delete(int id);

        /// <summary>测试发送（不写日志）</summary>
        Task<EmailTestSendResultDto> TestSendAsync(EmailTestSendRequest req);
    }
}
