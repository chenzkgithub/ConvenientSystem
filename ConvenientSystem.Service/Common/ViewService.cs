using System.Diagnostics.CodeAnalysis;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Common
{
    public class ViewService : IViewService
    {
        private readonly ILogger<ViewService> _logger;
        private readonly IFreeSql _configDb;

        public ViewService(
            ILogger<ViewService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public List<ViewDto> GetViews()
        {
            var views = _configDb.Select<SysViewEntity>()
                .OrderBy(v => v.SortOrder)
                .OrderBy(v => v.Id)
                .ToList(v => new ViewDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    Title = v.Title,
                    Component = v.Component,
                    RoutePath = v.RoutePath,
                    Description = v.Description,
                    Enabled = v.Enabled,
                    SortOrder = v.SortOrder,
                });

            // 批量加载权限点
            var viewIds = views.Select(v => v.Id).ToList();
            if (viewIds.Count > 0)
            {
                var perms = _configDb.Select<SysViewPermissionEntity>()
                    .Where(p => viewIds.Contains(p.ViewId))
                    .OrderBy(p => p.SortOrder)
                    .OrderBy(p => p.Id)
                    .ToList(p => new { p.Id, p.ViewId, p.Name, p.Title, p.SortOrder, p.Enabled });

                var permGroup = perms.GroupBy(p => p.ViewId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var view in views)
                {
                    if (permGroup.TryGetValue(view.Id, out var list))
                        view.Permissions = list.Select(p => new ViewPermissionDto
                        {
                            Id = p.Id, Name = p.Name, Title = p.Title, SortOrder = p.SortOrder, Enabled = p.Enabled,
                        }).ToList();
                }
            }
            return views;
        }

        public ViewSaveResultDto SaveView(ViewSaveDto dto)
        {
            try
            {
                var name = dto.Name.Trim();
                var title = dto.Title.Trim();
                if (string.IsNullOrEmpty(name))
                    return new ViewSaveResultDto { Ok = false, Msg = "权限码不能为空" };
                if (string.IsNullOrEmpty(title))
                    return new ViewSaveResultDto { Ok = false, Msg = "标题不能为空" };

                // 检查 Name 唯一性
                var dupQuery = _configDb.Select<SysViewEntity>().Where(v => v.Name == name);
                if (dto.Id > 0) dupQuery = dupQuery.Where(v => v.Id != dto.Id);
                if (dupQuery.Any())
                    return new ViewSaveResultDto { Ok = false, Msg = $"权限码 '{name}' 已存在" };

                if (dto.Id > 0)
                {
                    _configDb.Update<SysViewEntity>()
                        .Set(v => v.Name, name)
                        .Set(v => v.Title, title)
                        .Set(v => v.Component, string.IsNullOrWhiteSpace(dto.Component) ? null : dto.Component.Trim())
                        .Set(v => v.RoutePath, string.IsNullOrWhiteSpace(dto.RoutePath) ? null : dto.RoutePath.Trim())
                        .Set(v => v.Description, string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim())
                        .Set(v => v.Enabled, dto.Enabled)
                        .Where(v => v.Id == dto.Id)
                        .ExecuteAffrows();
                    _logger.LogInformation("编辑视图 Id={Id} Name={Name}", dto.Id, name);
                }
                else
                {
                    var maxSort = _configDb.Select<SysViewEntity>().Max(v => v.SortOrder);
                    var newId = (int)_configDb.Insert(new SysViewEntity
                    {
                        Name = name,
                        Title = title,
                        Component = string.IsNullOrWhiteSpace(dto.Component) ? null : dto.Component.Trim(),
                        RoutePath = string.IsNullOrWhiteSpace(dto.RoutePath) ? null : dto.RoutePath.Trim(),
                        Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                        Enabled = dto.Enabled,
                        SortOrder = maxSort + 1,
                    }).ExecuteIdentity();
                    _logger.LogInformation("新增视图 Id={Id} Name={Name}", newId, name);
                }
                return new ViewSaveResultDto { Ok = true };
            }
            catch (Exception ex)
            {
                return new ViewSaveResultDto { Ok = false, Msg = ex.Message };
            }
        }

        public void DeleteView(int id)
        {
            // 查询视图信息
            var view = _configDb.Select<SysViewEntity>().Where(v => v.Id == id).First();
            if (view == null) return;

            // 检查是否有菜单关联（按 Name 匹配）
            if (!string.IsNullOrEmpty(view.Name))
            {
                var hasMenu = _configDb.Select<SysMenuEntity>()
                    .Where(m => m.Name == view.Name && m.Enabled)
                    .Any();
                if (hasMenu)
                    throw new ArgumentException($"视图「{view.Title}」已被菜单关联，无法删除。请先移除关联的菜单项。");
            }

            _configDb.Transaction(() =>
            {
                // 先查权限点 Id（必须在删除前查）
                var permIds = _configDb.Select<SysViewPermissionEntity>().Where(p => p.ViewId == id).ToList(p => p.Id);

                // 删除权限点
                _configDb.Delete<SysViewPermissionEntity>().Where(p => p.ViewId == id).ExecuteAffrows();

                // 清理角色/用户授权
                if (permIds.Count > 0)
                {
                    _configDb.Delete<SysRoleViewPermEntity>().Where(e => permIds.Contains(e.ViewPermId)).ExecuteAffrows();
                    _configDb.Delete<SysUserViewPermEntity>().Where(e => permIds.Contains(e.ViewPermId)).ExecuteAffrows();
                }

                // 删除视图
                _configDb.Delete<SysViewEntity>().Where(v => v.Id == id).ExecuteAffrows();
            });
            _logger.LogInformation("删除视图 Id={Id} Name={Name}", id, view.Name);
        }

        public ViewSaveResultDto SavePermission(ViewPermissionSaveDto dto)
        {
            try
            {
                var name = dto.Name.Trim();
                var title = dto.Title.Trim();
                if (string.IsNullOrEmpty(name))
                    return new ViewSaveResultDto { Ok = false, Msg = "权限码不能为空" };
                if (string.IsNullOrEmpty(title))
                    return new ViewSaveResultDto { Ok = false, Msg = "标题不能为空" };

                // 检查同视图下 Name 唯一
                var dupQuery = _configDb.Select<SysViewPermissionEntity>()
                    .Where(p => p.ViewId == dto.ViewId && p.Name == name);
                if (dto.Id > 0) dupQuery = dupQuery.Where(p => p.Id != dto.Id);
                if (dupQuery.Any())
                    return new ViewSaveResultDto { Ok = false, Msg = $"权限码 '{name}' 在该视图下已存在" };

                if (dto.Id > 0)
                {
                    _configDb.Update<SysViewPermissionEntity>()
                        .Set(p => p.Name, name)
                        .Set(p => p.Title, title)
                        .Where(p => p.Id == dto.Id)
                        .ExecuteAffrows();
                }
                else
                {
                    var maxSort = _configDb.Select<SysViewPermissionEntity>()
                        .Where(p => p.ViewId == dto.ViewId).Max(p => p.SortOrder);
                    _configDb.Insert(new SysViewPermissionEntity
                    {
                        ViewId = dto.ViewId,
                        Name = name,
                        Title = title,
                        SortOrder = maxSort + 1,
                    }).ExecuteIdentity();
                }
                return new ViewSaveResultDto { Ok = true };
            }
            catch (Exception ex)
            {
                return new ViewSaveResultDto { Ok = false, Msg = ex.Message };
            }
        }

        public void DeletePermission(int id)
        {
            // 清理授权关联
            _configDb.Delete<SysRoleViewPermEntity>().Where(e => e.ViewPermId == id).ExecuteAffrows();
            _configDb.Delete<SysUserViewPermEntity>().Where(e => e.ViewPermId == id).ExecuteAffrows();
            _configDb.Delete<SysViewPermissionEntity>().Where(p => p.Id == id).ExecuteAffrows();
        }

        public List<MenuPermFlatDto> GetMenusWithViewPerms()
        {
            // 加载菜单
            var menus = _configDb.Select<SysMenuEntity>()
                .Where(m => m.Enabled)
                .OrderBy(m => m.SortOrder)
                .ToList(m => new MenuPermFlatDto
                {
                    Id = m.Id, ParentId = m.ParentId, Title = m.Title,
                    Name = m.Name, Type = m.Type,
                });

            // 加载视图权限点（按视图 Name 匹配菜单 Name）
            var menuNames = menus.Where(m => !string.IsNullOrEmpty(m.Name)).Select(m => m.Name!).Distinct().ToList();
            if (menuNames.Count > 0)
            {
                var views = _configDb.Select<SysViewEntity>()
                    .Where(v => menuNames.Contains(v.Name) && v.Enabled)
                    .ToList(v => new { v.Id, v.Name });
                var viewNameToId = views.ToDictionary(v => v.Name, v => v.Id);
                var viewIds = views.Select(v => v.Id).ToList();

                if (viewIds.Count > 0)
                {
                    var perms = _configDb.Select<SysViewPermissionEntity>()
                        .Where(p => viewIds.Contains(p.ViewId) && p.Enabled)
                        .OrderBy(p => p.SortOrder)
                        .ToList(p => new { p.Id, p.ViewId, p.Name, p.Title });
                    var permsByView = perms.GroupBy(p => p.ViewId).ToDictionary(g => g.Key, g => g.ToList());

                    // 给匹配的菜单项填充权限点
                    foreach (var menu in menus)
                    {
                        if (!string.IsNullOrEmpty(menu.Name) && viewNameToId.TryGetValue(menu.Name, out var viewId)
                            && permsByView.TryGetValue(viewId, out var permList))
                        {
                            menu.ViewPerms = permList.Select(p => new ViewPermNodeDto
                            {
                                Id = p.Id, Name = p.Name, Title = p.Title,
                            }).ToList();
                        }
                    }
                }
            }
            return menus;
        }

        // ========== 授权相关 ==========

        public List<int> GetRoleViewPermIds(int roleId)
            => _configDb.Select<SysRoleViewPermEntity>()
                .Where(e => e.RoleId == roleId).ToList(e => e.ViewPermId);

        public void SaveRoleViewPerms(int roleId, List<int> viewPermIds)
        {
            _configDb.Transaction(() =>
            {
                _configDb.Delete<SysRoleViewPermEntity>().Where(e => e.RoleId == roleId).ExecuteAffrows();
                var distinct = (viewPermIds ?? new List<int>()).Distinct().ToList();
                if (distinct.Count > 0)
                    _configDb.Insert(distinct.Select(pid => new SysRoleViewPermEntity { RoleId = roleId, ViewPermId = pid }).ToList())
                        .ExecuteAffrows();
            });
            _logger.LogInformation("更新角色视图权限 RoleId={RoleId}，权限点数={Count}", roleId, viewPermIds?.Count ?? 0);
        }

        public List<int> GetUserViewPermIds(Guid userId)
            => _configDb.Select<SysUserViewPermEntity>()
                .Where(e => e.UserId == userId).ToList(e => e.ViewPermId);

        public void SaveUserViewPerms(Guid userId, List<int> viewPermIds)
        {
            _configDb.Transaction(() =>
            {
                _configDb.Delete<SysUserViewPermEntity>().Where(e => e.UserId == userId).ExecuteAffrows();
                var distinct = (viewPermIds ?? new List<int>()).Distinct().ToList();
                if (distinct.Count > 0)
                    _configDb.Insert(distinct.Select(pid => new SysUserViewPermEntity { UserId = userId, ViewPermId = pid }).ToList())
                        .ExecuteAffrows();
            });
            _logger.LogInformation("更新用户视图权限 UserId={UserId}，权限点数={Count}", userId, viewPermIds?.Count ?? 0);
        }

        public List<string> GetViewPermCodes(Guid userId, List<int> roleIds)
        {
            // 角色级授权
            var rolePermIds = roleIds.Count == 0
                ? new List<int>()
                : _configDb.Select<SysRoleViewPermEntity>()
                    .Where(e => roleIds.Contains(e.RoleId))
                    .ToList(e => e.ViewPermId);

            // 用户级额外授权
            var userPermIds = _configDb.Select<SysUserViewPermEntity>()
                .Where(e => e.UserId == userId)
                .ToList(e => e.ViewPermId);

            var allPermIds = rolePermIds.Union(userPermIds).Distinct().ToList();
            if (allPermIds.Count == 0) return new List<string>();

            return _configDb.Select<SysViewPermissionEntity>()
                .Where(p => allPermIds.Contains(p.Id) && p.Enabled)
                .ToList(p => p.Name);
        }
    }
}
