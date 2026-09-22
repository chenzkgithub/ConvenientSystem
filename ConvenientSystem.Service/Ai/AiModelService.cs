using System.Diagnostics;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// AI 模型管理实现：ApiKey 经 DataProtection 加密存储（同 Apifox Access Token 先例），
    /// 任何出参都不带明文；唯一默认模型在每次 Save/Delete 后归一化（无启用默认时取首个启用模型）。
    /// </summary>
    public class AiModelService : IAiModelService
    {
        private const string ProtectorPurpose = "ConvenientSystem.Ai.ApiKey.v1";

        private readonly IFreeSql _db;
        private readonly IDataProtector _keyProtector;
        private readonly IAiCompletionProvider _provider;
        private readonly ILogger<AiModelService> _logger;

        public AiModelService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql db,
            IDataProtectionProvider dataProtectionProvider,
            IAiCompletionProvider provider,
            ILogger<AiModelService> logger)
        {
            _db = db;
            _keyProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
            _provider = provider;
            _logger = logger;
        }

        public List<AiModelListItemDto> List()
        {
            return _db.Select<AiModelEntity>()
                .OrderBy(m => m.SortOrder)
                .OrderBy(m => m.Id)
                .ToList(m => new AiModelListItemDto
                {
                    Id = m.Id,
                    Name = m.Name,
                    BaseUrl = m.BaseUrl,
                    ModelId = m.ModelId,
                    Scenes = m.Scenes,
                    IsDefault = m.IsDefault,
                    Enabled = m.Enabled,
                    HasKey = m.ApiKey != string.Empty,
                    MaxContextChars = m.MaxContextChars,
                    SortOrder = m.SortOrder,
                });
        }

        public AiModelListItemDto Save(AiModelSaveRequest request)
        {
            if (request == null) throw new BizException("请求不能为空");
            var name = request.Name?.Trim() ?? string.Empty;
            var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
            var modelId = request.ModelId?.Trim() ?? string.Empty;
            if (name.Length == 0) throw new BizException("请填写显示名称");
            if (baseUrl.Length == 0) throw new BizException("请填写接口地址");
            if (modelId.Length == 0) throw new BizException("请填写模型标识");
            var maxContext = request.MaxContextChars is (> 0 and <= 2_000_000) ? request.MaxContextChars : 60000;

            var apiKey = request.ApiKey?.Trim() ?? string.Empty;
            if (apiKey.Length > 2048) throw new BizException("API Key 长度不合法");

            AiModelEntity entity;
            if (request.Id > 0)
            {
                entity = _db.Select<AiModelEntity>().Where(m => m.Id == request.Id).First()
                    ?? throw new BizException("模型不存在或已被删除");
                entity.Name = name;
                entity.BaseUrl = baseUrl;
                entity.ModelId = modelId;
                entity.MaxContextChars = maxContext;
                entity.Enabled = request.Enabled;
                // ApiKey 留空 = 编辑时保持原 Key 不变（前端永不回传明文）
                if (apiKey.Length > 0)
                    entity.ApiKey = _keyProtector.Protect(apiKey);
            }
            else
            {
                entity = new AiModelEntity
                {
                    Name = name,
                    BaseUrl = baseUrl,
                    ModelId = modelId,
                    MaxContextChars = maxContext,
                    Enabled = request.Enabled,
                    SortOrder = (int)(_db.Select<AiModelEntity>().Max(m => (long?)m.SortOrder) ?? 0) + 1,
                    ApiKey = apiKey.Length > 0 ? _keyProtector.Protect(apiKey) : string.Empty,
                };
                entity.Id = (int)_db.Insert(entity).ExecuteIdentity();
            }

            _db.Update<AiModelEntity>(entity);
            NormalizeDefault();
            return List().First(m => m.Id == entity.Id);
        }

        public AiModelTestResult Test(AiModelTestRequest request)
        {
            if (request == null) throw new BizException("请求不能为空");
            var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
            var modelId = request.ModelId?.Trim() ?? string.Empty;
            if (baseUrl.Length == 0) throw new BizException("请填写接口地址");
            if (modelId.Length == 0) throw new BizException("请填写模型标识");

            var apiKey = request.ApiKey?.Trim() ?? string.Empty;
            if (apiKey.Length == 0 && request.Id > 0)
                apiKey = DecryptKey(_db.Select<AiModelEntity>().Where(m => m.Id == request.Id).First());

            var config = new AiModelConfig
            {
                Id = request.Id,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelId = modelId,
            };

            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = _provider.CompleteAsync(config, new List<AiPromptMessage>
                {
                    new() { Role = "user", Content = "ping" },
                }, cts.Token).GetAwaiter().GetResult();
                sw.Stop();
                var reply = (result.Content ?? string.Empty).Trim();
                // 上游 2xx 即连通；空回复也放行（有些模型对 ping 回空）
                return new AiModelTestResult { Ok = true, Message = "连接正常", ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
            catch (OperationCanceledException)
            {
                return new AiModelTestResult { Ok = false, Message = "连接超时（30 秒无响应）", ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
            catch (BizException ex)
            {
                sw.Stop();
                return new AiModelTestResult { Ok = false, Message = ex.Message, ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "AI 模型测试异常");
                return new AiModelTestResult { Ok = false, Message = $"连接失败：{ex.Message}", ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
        }

        public void SetDefault(int id)
        {
            var entity = _db.Select<AiModelEntity>().Where(m => m.Id == id).First()
                ?? throw new BizException("模型不存在或已被删除");
            if (!entity.Enabled) throw new BizException("禁用状态的模型不能设为默认");
            _db.Update<AiModelEntity>()
                .Set(m => m.IsDefault, false)
                .Where(m => m.IsDefault)
                .ExecuteAffrows();
            _db.Update<AiModelEntity>().Set(m => m.IsDefault, true).Where(m => m.Id == id).ExecuteAffrows();
        }

        public void Delete(int id)
        {
            if (_db.Delete<AiModelEntity>().Where(m => m.Id == id).ExecuteAffrows() == 0)
                throw new BizException("模型不存在或已被删除");
            NormalizeDefault();
        }

        public AiModelConfig ResolveUsableModel()
        {
            var enabled = _db.Select<AiModelEntity>().Where(m => m.Enabled).OrderBy(m => m.SortOrder).OrderBy(m => m.Id).ToList();
            if (enabled.Count == 0) throw new BizException("系统尚未配置可用的 AI 模型，请联系管理员在系统配置中添加");

            var chosen = enabled.FirstOrDefault(m => m.IsDefault)
                ?? enabled[0];

            return new AiModelConfig
            {
                Id = chosen.Id,
                Name = chosen.Name,
                BaseUrl = chosen.BaseUrl,
                ApiKey = DecryptKey(chosen),
                ModelId = chosen.ModelId,
                MaxContextChars = chosen.MaxContextChars,
            };
        }

        /// <summary>归一化唯一默认：无启用的默认模型时把首个启用模型设为默认；全无启用模型则清空默认</summary>
        private void NormalizeDefault()
        {
            var hasEnabledDefault = _db.Select<AiModelEntity>().Any(m => m.Enabled && m.IsDefault);
            if (hasEnabledDefault) return;
            var first = _db.Select<AiModelEntity>().Where(m => m.Enabled).OrderBy(m => m.SortOrder).OrderBy(m => m.Id).First();
            if (first == null) return;
            _db.Update<AiModelEntity>()
                .Set(m => m.IsDefault, false)
                .Where(m => m.IsDefault)
                .ExecuteAffrows();
            _db.Update<AiModelEntity>().Set(m => m.IsDefault, true).Where(m => m.Id == first.Id).ExecuteAffrows();
        }

        private string DecryptKey(AiModelEntity? entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.ApiKey)) return string.Empty;
            try
            {
                return _keyProtector.Unprotect(entity.ApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI ApiKey 解密失败（ModelId={ModelId}），按未配置处理", entity.Id);
                return string.Empty;
            }
        }
    }
}
