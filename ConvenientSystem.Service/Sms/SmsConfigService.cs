using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Sms;
using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信配置业务服务实现（列表化 CRUD + 配额 + 测试发送）。
    /// API 访问控制由 Controller 层的 PermissionAuthorize 负责。
    /// </summary>
    public class SmsConfigService : ISmsConfigService
    {
        private readonly IFreeSql _fsql;
        private readonly ISmsProviderFactory _providerFactory;
        private readonly ISmsQuotaService _quotaService;
        private readonly ICurrentUser _currentUser;

        public SmsConfigService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ISmsProviderFactory providerFactory,
            ISmsQuotaService quotaService,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _providerFactory = providerFactory;
            _quotaService = quotaService;
            _currentUser = currentUser;
        }

        /// <summary>获取全部短信配置列表（默认排前，关联模板名）</summary>
        public List<SmsProviderConfigDto> GetConfigs()
        {
            var configs = _fsql.Select<SmsProviderConfigEntity>()
                .OrderByDescending(c => c.IsDefault)
                .OrderByDescending(c => c.Id)
                .ToList();

            if (configs.Count == 0) return new List<SmsProviderConfigDto>();

            // 批量查模板名
            var templateIds = configs
                .Select(c => c.TemplateId).Where(t => t.HasValue)
                .Select(t => t!.Value).Distinct().ToList();
            var templateMap = templateIds.Count > 0
                ? _fsql.Select<SmsTemplateEntity>().Where(t => templateIds.Contains(t.Id)).ToList()
                    .ToDictionary(t => t.Id, t => t.Name)
                : new Dictionary<int, string>();

            return configs.Select(c =>
            {
                string? templateName = null;
                if (c.TemplateId.HasValue && templateMap.TryGetValue(c.TemplateId.Value, out var name))
                    templateName = name;
                return new SmsProviderConfigDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ProviderType = c.ProviderType,
                    AccessKeyId = c.AccessKeyId,
                    AccessKeySecret = c.AccessKeySecret,
                    DefaultSignature = c.DefaultSignature,
                    TemplateCode = c.TemplateCode,
                    TemplateId = c.TemplateId,
                    TemplateName = templateName,
                    IsDefault = c.IsDefault,
                    Enabled = c.Enabled,
                    CreateTime = c.CreateTime,
                    UpdateTime = c.UpdateTime
                };
            }).ToList();
        }

        /// <summary>新增或更新短信配置（Id<=0 新增，否则更新）</summary>
        public void Save(SmsProviderConfigDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("配置名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.ProviderType))
                throw new BadRequestException("服务商类型不能为空");

            try
            {
                var entity = new SmsProviderConfigEntity
                {
                    Name = dto.Name.Trim(),
                    ProviderType = dto.ProviderType,
                    AccessKeyId = dto.AccessKeyId ?? string.Empty,
                    AccessKeySecret = dto.AccessKeySecret ?? string.Empty,
                    DefaultSignature = string.IsNullOrWhiteSpace(dto.DefaultSignature) ? "zk" : dto.DefaultSignature,
                    TemplateCode = dto.TemplateCode ?? string.Empty,
                    TemplateId = dto.TemplateId,
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
                    _fsql.Update<SmsProviderConfigEntity>()
                        .Set(c => c.Name, entity.Name)
                        .Set(c => c.ProviderType, entity.ProviderType)
                        .Set(c => c.AccessKeyId, entity.AccessKeyId)
                        .Set(c => c.AccessKeySecret, entity.AccessKeySecret)
                        .Set(c => c.DefaultSignature, entity.DefaultSignature)
                        .Set(c => c.TemplateCode, entity.TemplateCode)
                        .Set(c => c.TemplateId, entity.TemplateId)
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
                throw new BizException("保存短信配置失败：" + ex.Message, StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>删除短信配置</summary>
        public void Delete(int id)
        {
            _fsql.Delete<SmsProviderConfigEntity>().Where(c => c.Id == id).ExecuteAffrows();
        }

        public IReadOnlyCollection<string> GetProviderNames() => _providerFactory.GetRegisteredNames();

        public SmsQuotaDto GetQuota()
        {
            return _quotaService.GetQuotaStatus();
        }

        public void SaveQuota(SmsQuotaDto dto)
        {
            SaveQuotaValue("Daily", dto.DailyMax);
            SaveQuotaValue("Monthly", dto.MonthlyMax);
        }

        public async Task<SmsTestSendResultDto> TestSendAsync(SmsTestSendRequest req)
        {
            if (!SmsPhoneHelper.IsValid(req.Phone)) throw new BadRequestException("手机号格式错误");
            if (string.IsNullOrWhiteSpace(req.Content)) throw new BadRequestException("短信内容不能为空");

            var freqCheck = _quotaService.CheckFrequency(req.Phone);
            if (!freqCheck.ok) throw new BadRequestException(freqCheck.message ?? "发送频率超限");

            try
            {
                var provider = _providerFactory.GetProvider();
                var result = await provider.SendAsync(req.Phone, req.Content, req.Signature);
                return new SmsTestSendResultDto
                {
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage,
                    ProviderMsgId = result.ProviderMsgId,
                    CostMs = result.CostMs,
                    Provider = provider.Name
                };
            }
            catch (Exception ex)
            {
                // 发送失败不作为异常抛出：前端需要展示服务商返回的原始错误
                return new SmsTestSendResultDto
                {
                    Success = false,
                    ErrorMessage = "发送异常：" + ex.Message,
                    ProviderMsgId = null,
                    CostMs = 0,
                    Provider = "unknown"
                };
            }
        }

        private void SaveQuotaValue(string quotaType, int maxCount)
        {
            var existing = _fsql.Select<SmsQuotaEntity>()
                .Where(q => q.QuotaType == quotaType)
                .First();
            if (existing == null)
            {
                _fsql.Insert(new SmsQuotaEntity
                {
                    QuotaType = quotaType,
                    MaxCount = maxCount,
                    UpdateTime = DateTime.Now
                }).ExecuteAffrows();
            }
            else
            {
                _fsql.Update<SmsQuotaEntity>()
                    .Set(q => q.MaxCount, maxCount)
                    .Set(q => q.UpdateTime, DateTime.Now)
                    .Where(q => q.Id == existing.Id)
                    .ExecuteAffrows();
            }
        }
    }
}
