using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Email;

namespace ConvenientSystem.Service.Email
{
    /// <summary>
    /// 邮件配置业务服务实现（列表化 CRUD + 测试发送）。
    /// API 访问控制由 Controller 层的 PermissionAuthorize 负责。
    /// </summary>
    public class EmailConfigService : IEmailConfigService
    {
        /// <summary>密码回显占位符：前端原样回传表示未修改密码</summary>
        private const string PasswordPlaceholder = "••••••••";

        private readonly IFreeSql _fsql;
        private readonly EmailSendJob _emailSendJob;
        private readonly ICurrentUser _currentUser;

        public EmailConfigService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            EmailSendJob emailSendJob,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _emailSendJob = emailSendJob;
            _currentUser = currentUser;
        }

        /// <summary>获取全部邮件配置列表（默认排前，密码字段脱敏）</summary>
        public List<EmailConfigDto> GetConfigs()
        {
            var configs = _fsql.Select<EmailConfigEntity>()
                .OrderByDescending(c => c.IsDefault)
                .OrderByDescending(c => c.Id)
                .ToList();

            return configs.Select(c =>
            {
                var decrypted = AesEncryptHelper.Decrypt(c.Password);
                return new EmailConfigDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    SmtpServer = c.SmtpServer,
                    SmtpPort = c.SmtpPort,
                    Account = c.Account,
                    Password = !string.IsNullOrEmpty(decrypted) ? PasswordPlaceholder : "",
                    FromName = c.FromName,
                    EnableSsl = c.EnableSsl,
                    IsDefault = c.IsDefault,
                    Enabled = c.Enabled,
                    CreateTime = c.CreateTime,
                    UpdateTime = c.UpdateTime
                };
            }).ToList();
        }

        /// <summary>新增或更新邮件配置（Id<=0 新增，否则更新）；密码为占位符时保留原密码</summary>
        public void Save(EmailConfigDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("配置名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.SmtpServer))
                throw new BadRequestException("SMTP 服务器不能为空");
            if (string.IsNullOrWhiteSpace(dto.Account))
                throw new BadRequestException("发件人邮箱不能为空");
            if (dto.SmtpPort <= 0 || dto.SmtpPort > 65535)
                throw new BadRequestException("端口号无效");

            try
            {
                // 密码处理：如果是占位符且是编辑模式，保留原密码
                var existing = dto.Id > 0
                    ? _fsql.Select<EmailConfigEntity>().Where(c => c.Id == dto.Id).First()
                    : null;

                var passwordToSave = dto.Password == PasswordPlaceholder && existing != null
                    ? existing.Password
                    : AesEncryptHelper.Encrypt(dto.Password);

                var entity = new EmailConfigEntity
                {
                    Name = dto.Name.Trim(),
                    SmtpServer = dto.SmtpServer.Trim(),
                    SmtpPort = dto.SmtpPort,
                    Account = dto.Account.Trim(),
                    Password = passwordToSave,
                    FromName = (dto.FromName ?? "系统通知").Trim(),
                    EnableSsl = dto.EnableSsl,
                    IsDefault = dto.IsDefault,
                    Enabled = dto.Enabled,
                    UpdateTime = DateTime.Now
                };

                if (dto.Id <= 0)
                {
                    _fsql.Insert(entity).ExecuteAffrows();
                }
                else
                {
                    _fsql.Update<EmailConfigEntity>()
                        .Set(c => c.Name, entity.Name)
                        .Set(c => c.SmtpServer, entity.SmtpServer)
                        .Set(c => c.SmtpPort, entity.SmtpPort)
                        .Set(c => c.Account, entity.Account)
                        .Set(c => c.Password, entity.Password)
                        .Set(c => c.FromName, entity.FromName)
                        .Set(c => c.EnableSsl, entity.EnableSsl)
                        .Set(c => c.IsDefault, entity.IsDefault)
                        .Set(c => c.Enabled, entity.Enabled)
                        .Set(c => c.UpdateTime, entity.UpdateTime)
                        .Where(c => c.Id == dto.Id)
                        .ExecuteAffrows();
                }
            }
            catch (BizException) { throw; }
            catch (Exception ex)
            {
                throw new BizException("保存邮件配置失败：" + ex.Message, StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>删除邮件配置</summary>
        public void Delete(int id)
        {
            _fsql.Delete<EmailConfigEntity>().Where(c => c.Id == id).ExecuteAffrows();
        }

        public async Task<EmailTestSendResultDto> TestSendAsync(EmailTestSendRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Recipients))
                throw new BadRequestException("收件人不能为空");
            if (string.IsNullOrWhiteSpace(req.Subject))
                throw new BadRequestException("邮件主题不能为空");
            if (string.IsNullOrWhiteSpace(req.Content))
                throw new BadRequestException("邮件内容不能为空");

            var result = await _emailSendJob.TestSendAsync(req.Recipients, req.Subject, req.Content);
            return new EmailTestSendResultDto
            {
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                CostMs = result.CostMs
            };
        }
    }
}
