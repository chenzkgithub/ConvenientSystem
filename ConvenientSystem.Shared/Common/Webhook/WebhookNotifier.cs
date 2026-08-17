using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 群机器人发送核心（位于 Shared，供 Shared 层的 Job 与 Service 层共用，避免 Job 反向依赖 Service）。
    /// 负责：从配置库读取启用配置、解密 Secret、经工厂路由到对应 Provider 发送。
    /// </summary>
    public class WebhookNotifier
    {
        private readonly IFreeSql _fsql;
        private readonly WebhookProviderFactory _factory;
        private readonly ILogger<WebhookNotifier> _logger;

        public WebhookNotifier(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            WebhookProviderFactory factory,
            ILogger<WebhookNotifier> logger)
        {
            _fsql = fsql;
            _factory = factory;
            _logger = logger;
        }

        /// <summary>
        /// 对单条配置发送：按 EnableGroup / EnablePrivate 分别路由到群 Provider（{base}）与私聊 Provider（{base}-private）。
        /// ProviderType 只存基础类型 dingtalk / wecom / feishu；两者都启用则各发一次，全部成功才算成功。
        /// Secret 与 AppSecret 解密为明文供 Provider 加签 / 换 token 使用（原地替换，仅作用于本次内存副本）。
        /// </summary>
        public async Task<WebhookSendResult> SendOneAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            // 解密密文密钥为明文供 Provider 加签 / 换 token 使用（原地替换，仅作用于本次内存副本）
            if (!string.IsNullOrEmpty(cfg.Secret))
                cfg.Secret = AesEncryptHelper.Decrypt(cfg.Secret);
            if (!string.IsNullOrEmpty(cfg.AppSecret))
                cfg.AppSecret = AesEncryptHelper.Decrypt(cfg.AppSecret);

            var baseType = cfg.ProviderType;
            var results = new List<WebhookSendResult>();
            var sentAny = false;

            if (cfg.EnableGroup)
            {
                var provider = _factory.Get(baseType);
                WebhookSendResult groupResult;
                if (provider == null)
                    groupResult = WebhookSendResult.Fail("不支持的服务商类型：" + baseType, 0);
                else
                {
                    groupResult = await provider.SendAsync(cfg, title, content);
                    sentAny = true;
                }
                results.Add(groupResult);
                await WriteLogAsync(cfg.Id, cfg.Name, baseType, title, content, groupResult);
            }

            if (cfg.EnablePrivate)
            {
                var provider = _factory.Get(baseType + "-private");
                WebhookSendResult privateResult;
                if (provider == null)
                    privateResult = WebhookSendResult.Fail("不支持的私聊服务商类型：" + baseType, 0);
                else
                {
                    privateResult = await provider.SendAsync(cfg, title, content);
                    sentAny = true;
                }
                results.Add(privateResult);
                await WriteLogAsync(cfg.Id, cfg.Name, baseType, title, content, privateResult);
            }

            if (!sentAny)
            {
                var noModeResult = WebhookSendResult.Fail("配置未启用任何发送模式", 0);
                await WriteLogAsync(cfg.Id, cfg.Name, baseType, title, content, noModeResult);
                return noModeResult;
            }

            var failed = results.Where(r => !r.Success).ToList();
            if (failed.Count == 0)
                return WebhookSendResult.Ok(results.Max(r => r.CostMs));
            return WebhookSendResult.Fail(string.Join("; ", failed.Select(r => r.ErrorMessage)), results.Max(r => r.CostMs));
        }

        /// <summary>写入机器人发送日志（成功/失败都记；异常吞掉不影响发送流程）。</summary>
        private async Task WriteLogAsync(int configId, string configName, string providerType,
            string title, string content, WebhookSendResult result)
        {
            try
            {
                var truncated = content.Length > 2000 ? content[..2000] : content;
                _fsql.Insert(new SysWebhookLogEntity
                {
                    ConfigId = configId,
                    ConfigName = configName,
                    ProviderType = providerType,
                    Title = title,
                    Content = truncated,
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage,
                    CostMs = result.CostMs
                }).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入机器人发送日志失败");
            }
        }

        /// <summary>向所有启用的机器人广播一条消息（用于任务失败等事件；任何异常都吞掉，绝不影响调用方）。</summary>
        public async Task SendToAllEnabledAsync(string title, string content)
        {
            try
            {
                var configs = _fsql.Select<SysWebhookConfigEntity>().Where(c => c.Enabled).ToList();
                foreach (var cfg in configs)
                {
                    try
                    {
                        var result = await SendOneAsync(cfg, title, content);
                        if (!result.Success)
                            _logger.LogWarning("群机器人[{Name}]推送失败：{Error}", cfg.Name, result.ErrorMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "群机器人[{Name}]推送异常", cfg.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "群机器人广播失败");
            }
        }

        /// <summary>向标记为默认的机器人推送（用于通知联动、任务推送等自动场景；异常吞掉不影响调用方）。</summary>
        public async Task SendToDefaultAsync(string title, string content)
        {
            try
            {
                var configs = _fsql.Select<SysWebhookConfigEntity>()
                    .Where(c => c.IsDefault && c.Enabled).ToList();
                if (configs.Count == 0)
                {
                    _logger.LogWarning("无默认机器人配置，跳过推送：{Title}", title);
                    return;
                }
                foreach (var cfg in configs)
                {
                    try
                    {
                        var result = await SendOneAsync(cfg, title, content);
                        if (!result.Success)
                            _logger.LogWarning("默认机器人[{Name}]推送失败：{Error}", cfg.Name, result.ErrorMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "默认机器人[{Name}]推送异常", cfg.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "默认机器人推送失败");
            }
        }
    }
}
