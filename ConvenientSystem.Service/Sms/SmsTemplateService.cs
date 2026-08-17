using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Sms;
using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信模板业务服务实现（模板数据存放在配置库）。
    /// </summary>
    public class SmsTemplateService : ISmsTemplateService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public SmsTemplateService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        private bool IsDataScopeAll => _currentUser.DataScope == DataScope.All;
        private bool IsOwner(Guid? createdById)
            => _currentUser.UserId.HasValue && createdById == _currentUser.UserId;

        public List<SmsTemplateDto> GetList(string? category, string? keyword)
        {
            var query = _fsql.Select<SmsTemplateEntity>();
            if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                query = query.Where(t => t.CreatedById == _currentUser.UserId);
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(t => t.Category == category);
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(t => t.Name.Contains(keyword) || t.Content.Contains(keyword));
            var templates = query.OrderByDescending(t => t.CreateTime).ToList();
            // 创建人账号与姓名关联 SysUser 查询
            var userMap = UserDisplayHelper.GetMap(_fsql, templates.Select(t => t.CreatedById));
            return templates.Select(t => ToDto(t, userMap)).ToList();
        }

        public SmsTemplateDto Get(int id)
        {
            var entity = _fsql.Select<SmsTemplateEntity>().Where(x => x.Id == id).First()
                ?? throw new NotFoundException("模板不存在");
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new NotFoundException("模板不存在");
            return ToDto(entity, UserDisplayHelper.GetMap(_fsql, new[] { entity.CreatedById }));
        }

        public SmsTemplateDto Create(SmsTemplateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new BadRequestException("模板名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.Content)) throw new BadRequestException("模板内容不能为空");

            var entity = new SmsTemplateEntity
            {
                Name = dto.Name.Trim(),
                Content = dto.Content,
                Signature = string.IsNullOrWhiteSpace(dto.Signature) ? "zk" : dto.Signature,
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "通知" : dto.Category,
                Enabled = dto.Enabled,
                CreatedById = _currentUser.UserId,
                UpdateTime = DateTime.Now
            };
            entity.Id = (int)_fsql.Insert(entity).ExecuteIdentity();
            entity.CreateTime = DateTime.Now;
            return ToDto(entity, UserDisplayHelper.GetMap(_fsql, new[] { entity.CreatedById }));
        }

        public void Update(SmsTemplateDto dto)
        {
            var entity = _fsql.Select<SmsTemplateEntity>().Where(x => x.Id == dto.Id).First()
                ?? throw new NotFoundException("模板不存在");
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new NotFoundException("模板不存在");
            entity.Name = dto.Name.Trim();
            entity.Content = dto.Content;
            entity.Signature = dto.Signature;
            entity.Category = dto.Category;
            entity.Enabled = dto.Enabled;
            entity.UpdateTime = DateTime.Now;
            _fsql.Update<SmsTemplateEntity>().SetSource(entity).ExecuteAffrows();
        }

        public void Delete(int id)
        {
            var entity = _fsql.Select<SmsTemplateEntity>().Where(x => x.Id == id).First()
                ?? throw new NotFoundException("模板不存在");
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new NotFoundException("模板不存在");
            _fsql.Delete<SmsTemplateEntity>().Where(x => x.Id == id).ExecuteAffrows();
        }

        public ToggleEnabledDto ToggleEnabled(int id)
        {
            var entity = _fsql.Select<SmsTemplateEntity>().Where(x => x.Id == id).First()
                ?? throw new NotFoundException("模板不存在");
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new NotFoundException("模板不存在");
            entity.Enabled = !entity.Enabled;
            entity.UpdateTime = DateTime.Now;
            _fsql.Update<SmsTemplateEntity>().SetSource(entity).ExecuteAffrows();
            return new ToggleEnabledDto { Enabled = entity.Enabled };
        }

        public TemplatePreviewDto Preview(PreviewTemplateRequest req)
        {
            return new TemplatePreviewDto
            {
                Rendered = SmsTemplateRenderer.Render(req.Content, req.Variables ?? new())
            };
        }

        public List<string> ExtractVariables(string content)
        {
            return SmsTemplateRenderer.ExtractVariables(content);
        }

        private static SmsTemplateDto ToDto(SmsTemplateEntity t, Dictionary<Guid, UserDisplayHelper.UserDisplay> userMap)
        {
            var creator = UserDisplayHelper.Find(userMap, t.CreatedById);
            return new SmsTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Content = t.Content,
                Signature = t.Signature,
                Category = t.Category,
                Enabled = t.Enabled,
                CreatedByAccount = creator?.Account,
                CreatedByName = creator?.DisplayName,
                CreateTime = t.CreateTime
            };
        }
    }
}
