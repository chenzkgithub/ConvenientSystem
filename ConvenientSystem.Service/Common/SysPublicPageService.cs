using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 外部公开页面服务实现：CRUD + ListEnabled（无需鉴权，供前端路由注册）。
    /// 存储于本地配置库 ConvenientSystem 的 SysPublicPage 表。
    /// </summary>
    public class SysPublicPageService : ISysPublicPageService
    {
        private readonly IFreeSql _configDb;

        public SysPublicPageService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _configDb = configDb;
        }

        public List<SysPublicPageItemDto> ListEnabled()
        {
            return _configDb.Select<SysPublicPageEntity>()
                .Where(e => e.Enabled)
                .OrderBy(e => e.SortOrder)
                .OrderBy(e => e.Id)
                .ToList(e => new SysPublicPageItemDto
                {
                    Id = e.Id,
                    PageKey = e.PageKey,
                    Title = e.Title,
                    Component = e.Component,
                    Description = e.Description,
                    Enabled = e.Enabled,
                    SortOrder = e.SortOrder,
                });
        }

        public List<SysPublicPageItemDto> GetAll()
        {
            return _configDb.Select<SysPublicPageEntity>()
                .OrderBy(e => e.SortOrder)
                .OrderBy(e => e.Id)
                .ToList(e => new SysPublicPageItemDto
                {
                    Id = e.Id,
                    PageKey = e.PageKey,
                    Title = e.Title,
                    Component = e.Component,
                    Description = e.Description,
                    Enabled = e.Enabled,
                    SortOrder = e.SortOrder,
                });
        }

        public int Create(SysPublicPageCreateDto dto)
        {
            var key = dto.PageKey?.Trim() ?? "";
            if (!key.StartsWith('/')) key = "/" + key;
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("路由路径不能为空");
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("显示名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.Component))
                throw new ArgumentException("组件路径不能为空");

            // PageKey 唯一性检查
            var exists = _configDb.Select<SysPublicPageEntity>()
                .Where(e => e.PageKey == key)
                .Any();
            if (exists)
                throw new ArgumentException($"路由路径 {key} 已存在");

            var entity = new SysPublicPageEntity
            {
                PageKey = key,
                Title = dto.Title.Trim(),
                Component = dto.Component.Trim(),
                Description = dto.Description?.Trim(),
                Enabled = dto.Enabled,
                SortOrder = dto.SortOrder,
            };
            var rows = _configDb.Insert(entity).ExecuteAffrows();
            if (rows > 0) return _configDb.Select<SysPublicPageEntity>()
                .Where(e => e.PageKey == key)
                .First(e => e.Id);
            return 0;
        }

        public void Update(SysPublicPageUpdateDto dto)
        {
            var key = dto.PageKey?.Trim() ?? "";
            if (!key.StartsWith('/')) key = "/" + key;
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("路由路径不能为空");
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("显示名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.Component))
                throw new ArgumentException("组件路径不能为空");

            // PageKey 唯一性检查（排除自身）
            var dup = _configDb.Select<SysPublicPageEntity>()
                .Where(e => e.PageKey == key && e.Id != dto.Id)
                .Any();
            if (dup)
                throw new ArgumentException($"路由路径 {key} 已被其他页面占用");

            _configDb.Update<SysPublicPageEntity>()
                .Set(e => e.PageKey, key)
                .Set(e => e.Title, dto.Title.Trim())
                .Set(e => e.Component, dto.Component.Trim())
                .Set(e => e.Description, dto.Description?.Trim())
                .Set(e => e.Enabled, dto.Enabled)
                .Set(e => e.SortOrder, dto.SortOrder)
                .Set(e => e.UpdatedAt, DateTime.UtcNow)
                .Where(e => e.Id == dto.Id)
                .ExecuteAffrows();
        }

        public void Delete(int id)
        {
            _configDb.Delete<SysPublicPageEntity>()
                .Where(e => e.Id == id)
                .ExecuteAffrows();
        }
    }
}
