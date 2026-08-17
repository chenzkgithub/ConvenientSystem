using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 群 + 私聊机器人通知业务服务实现：配置存配置库，Secret/AppSecret AES 加密；发送经 Provider 工厂路由。
    /// 一条配置支持两种模式：EnableGroup 控制群机器人，EnablePrivate 控制私聊机器人。
    /// </summary>
    public class NotifyService : INotifyService
    {
        private readonly IFreeSql _fsql;
        private readonly WebhookProviderFactory _factory;
        private readonly WebhookNotifier _notifier;
        private readonly ILogger<NotifyService> _logger;

        public NotifyService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            WebhookProviderFactory factory,
            WebhookNotifier notifier,
            ILogger<NotifyService> logger)
        {
            _fsql = fsql;
            _factory = factory;
            _notifier = notifier;
            _logger = logger;
        }

        public List<WebhookConfigDto> GetConfigs()
        {
            return _fsql.Select<SysWebhookConfigEntity>()
                .OrderByDescending(c => c.Id)
                .ToList()
                .Select(c => new WebhookConfigDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ProviderType = c.ProviderType,
                    WebhookUrl = c.WebhookUrl,
                    Secret = string.IsNullOrEmpty(c.Secret) ? string.Empty : AesEncryptHelper.Decrypt(c.Secret),
                    AppKey = c.AppKey,
                    AppSecret = string.IsNullOrEmpty(c.AppSecret) ? string.Empty : AesEncryptHelper.Decrypt(c.AppSecret),
                    RecipientIds = c.RecipientIds,
                    EnableGroup = c.EnableGroup,
                    EnablePrivate = c.EnablePrivate,
                    UseCard = c.UseCard,
                    IsDefault = c.IsDefault,
                    Enabled = c.Enabled,
                    CreateTime = c.CreateTime,
                    UpdateTime = c.UpdateTime
                }).ToList();
        }

        public IReadOnlyCollection<string> GetProviderTypes() => _factory.GetBaseTypes();

        public void Save(WebhookConfigDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("名称不能为空");
            if (!dto.EnableGroup && !dto.EnablePrivate)
                throw new BadRequestException("至少启用一种发送模式（群或私聊）");
            if (dto.EnableGroup && string.IsNullOrWhiteSpace(dto.WebhookUrl))
                throw new BadRequestException("群模式：Webhook 地址不能为空");
            if (dto.EnablePrivate && string.IsNullOrWhiteSpace(dto.AppKey))
                throw new BadRequestException("私聊模式：AppKey 不能为空");
            if (dto.EnablePrivate && string.IsNullOrWhiteSpace(dto.AppSecret))
                throw new BadRequestException("私聊模式：AppSecret 不能为空");
            if (dto.EnablePrivate && string.IsNullOrWhiteSpace(dto.RecipientIds))
                throw new BadRequestException("私聊模式：接收者列表不能为空");
            if (!_factory.GetBaseTypes().Contains(dto.ProviderType))
                throw new BadRequestException("不支持的服务商类型：" + dto.ProviderType);
            if (dto.EnablePrivate && _factory.Get(dto.ProviderType + "-private") == null)
                throw new BadRequestException("该服务商不支持私聊模式：" + dto.ProviderType);

            try
            {
                var entity = new SysWebhookConfigEntity
                {
                    Name = dto.Name.Trim(),
                    ProviderType = dto.ProviderType,
                    WebhookUrl = dto.WebhookUrl?.Trim() ?? string.Empty,
                    Secret = string.IsNullOrEmpty(dto.Secret) ? null : AesEncryptHelper.Encrypt(dto.Secret),
                    AppKey = string.IsNullOrEmpty(dto.AppKey) ? null : dto.AppKey.Trim(),
                    AppSecret = string.IsNullOrEmpty(dto.AppSecret) ? null : AesEncryptHelper.Encrypt(dto.AppSecret),
                    RecipientIds = string.IsNullOrEmpty(dto.RecipientIds) ? null : dto.RecipientIds.Trim(),
                    EnableGroup = dto.EnableGroup,
                    EnablePrivate = dto.EnablePrivate,
                    UseCard = dto.UseCard,
                    IsDefault = dto.IsDefault,
                    Enabled = dto.Enabled,
                    UpdateTime = DateTime.Now
                };

                if (dto.Id <= 0)
                {
                    _fsql.Insert(entity).ExecuteAffrows();
                    _logger.LogInformation("新增机器人配置：{Name} ({ProviderType})", entity.Name, entity.ProviderType);
                }
                else
                {
                    var existing = _fsql.Select<SysWebhookConfigEntity>().Where(c => c.Id == dto.Id).First()
                        ?? throw new NotFoundException("配置不存在");
                    // Secret/AppSecret 为空表示不修改，保留原密文
                    var secret = string.IsNullOrEmpty(dto.Secret) ? existing.Secret : AesEncryptHelper.Encrypt(dto.Secret);
                    var appSecret = string.IsNullOrEmpty(dto.AppSecret) ? existing.AppSecret : AesEncryptHelper.Encrypt(dto.AppSecret);
                    _fsql.Update<SysWebhookConfigEntity>()
                        .Set(c => c.Name, entity.Name)
                        .Set(c => c.ProviderType, entity.ProviderType)
                        .Set(c => c.WebhookUrl, entity.WebhookUrl)
                        .Set(c => c.Secret, secret)
                        .Set(c => c.AppKey, entity.AppKey)
                        .Set(c => c.AppSecret, appSecret)
                        .Set(c => c.RecipientIds, entity.RecipientIds)
                        .Set(c => c.EnableGroup, entity.EnableGroup)
                        .Set(c => c.EnablePrivate, entity.EnablePrivate)
                        .Set(c => c.UseCard, entity.UseCard)
                        .Set(c => c.IsDefault, entity.IsDefault)
                        .Set(c => c.Enabled, entity.Enabled)
                        .Set(c => c.UpdateTime, entity.UpdateTime)
                        .Where(c => c.Id == dto.Id)
                        .ExecuteAffrows();
                    _logger.LogInformation("更新机器人配置：{Name} ({ProviderType})", entity.Name, entity.ProviderType);
                }
            }
            catch (BizException) { throw; }
            catch (Exception ex)
            {
                throw new BizException("保存机器人配置失败：" + ex.Message, StatusCodes.Status500InternalServerError);
            }
        }

        public void Delete(int id)
        {
            _fsql.Delete<SysWebhookConfigEntity>().Where(c => c.Id == id).ExecuteAffrows();
        }

        public async Task<WebhookSendResultDto> TestAsync(int id)
        {
            var cfg = _fsql.Select<SysWebhookConfigEntity>().Where(c => c.Id == id).First()
                ?? throw new NotFoundException("配置不存在");

            if (!cfg.EnableGroup && !cfg.EnablePrivate)
                throw new BadRequestException("配置未启用任何发送模式");

            var result = await _notifier.SendOneAsync(cfg, "测试消息",
                $"这是一条来自 ConvenientSystem 的测试推送。\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return new WebhookSendResultDto
            {
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                CostMs = result.CostMs
            };
        }
    }
}
