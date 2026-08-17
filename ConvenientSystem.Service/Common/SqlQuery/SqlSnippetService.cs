using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.Data.SqlClient;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 快捷输入管理服务实现：配置存储在本地配置库 SysSqlSnippet 表，Shortcut 唯一。
    /// </summary>
    public class SqlSnippetService : ISqlSnippetService
    {
        private readonly IFreeSql _configDb;
        private readonly ICurrentUser _currentUser;

        public SqlSnippetService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            ICurrentUser currentUser)
        {
            _configDb = configDb;
            _currentUser = currentUser;
        }

        private bool IsDataScopeAll => _currentUser.DataScope == DataScope.All;
        private bool IsOwner(Guid? createdById)
            => _currentUser.UserId.HasValue && createdById == _currentUser.UserId;
        private void EnsureOwner(SysSqlSnippetEntity entity)
        {
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new ForbiddenException("无权操作该快捷输入");
        }

        public List<SnippetDto> GetList()
        {
            try
            {
                var query = _configDb.Select<SysSqlSnippetEntity>()
                    .OrderBy(s => s.SortOrder)
                    .OrderBy(s => s.Id);
                if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                    query = query.Where(s => s.CreatedById == _currentUser.UserId);
                return query.ToList(s => new SnippetDto
                {
                    Id = s.Id,
                    Shortcut = s.Shortcut,
                    Expansion = s.Expansion,
                    Remark = s.Remark,
                    SortOrder = s.SortOrder
                });
            }
            catch (Exception ex)
            {
                throw new BizException($"读取快捷输入失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Add(SnippetDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Shortcut) || string.IsNullOrWhiteSpace(dto.Expansion))
                throw new BadRequestException("快捷输入和展开内容不能为空");
            try
            {
                var shortcut = dto.Shortcut.Trim();
                if (_configDb.Select<SysSqlSnippetEntity>().Where(s => s.Shortcut == shortcut).Any())
                    throw new BadRequestException($"快捷输入 '{shortcut}' 已存在");
                _configDb.Insert(new SysSqlSnippetEntity
                {
                    Shortcut = shortcut,
                    Expansion = dto.Expansion,
                    Remark = dto.Remark?.Trim(),
                    SortOrder = dto.SortOrder,
                    CreatedById = _currentUser.UserId
                }).ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                // 唯一索引冲突（并发插入同一简写）
                throw new BadRequestException($"快捷输入 '{dto.Shortcut}' 已存在");
            }
            catch (Exception ex)
            {
                throw new BizException($"添加快捷输入失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Update(SnippetDto dto)
        {
            if (dto.Id <= 0)
                throw new BadRequestException("缺少主键 Id");
            if (string.IsNullOrWhiteSpace(dto.Shortcut) || string.IsNullOrWhiteSpace(dto.Expansion))
                throw new BadRequestException("快捷输入和展开内容不能为空");
            try
            {
                var entity = _configDb.Select<SysSqlSnippetEntity>().Where(s => s.Id == dto.Id).First()
                    ?? throw new NotFoundException("快捷输入不存在");
                EnsureOwner(entity);
                var shortcut = dto.Shortcut.Trim();
                if (_configDb.Select<SysSqlSnippetEntity>().Where(s => s.Shortcut == shortcut && s.Id != dto.Id).Any())
                    throw new BadRequestException($"快捷输入 '{shortcut}' 已存在");
                _configDb.Update<SysSqlSnippetEntity>()
                    .Set(s => s.Shortcut, shortcut)
                    .Set(s => s.Expansion, dto.Expansion)
                    .Set(s => s.Remark, dto.Remark?.Trim())
                    .Set(s => s.SortOrder, dto.SortOrder)
                    .Where(s => s.Id == dto.Id)
                    .ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                throw new BadRequestException($"快捷输入 '{dto.Shortcut}' 已存在");
            }
            catch (Exception ex)
            {
                throw new BizException($"修改快捷输入失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Remove(SnippetDto dto)
        {
            if (dto.Id <= 0)
                throw new BadRequestException("缺少主键 Id");
            try
            {
                var entity = _configDb.Select<SysSqlSnippetEntity>().Where(s => s.Id == dto.Id).First()
                    ?? throw new NotFoundException("快捷输入不存在");
                EnsureOwner(entity);
                var removed = _configDb.Delete<SysSqlSnippetEntity>()
                    .Where(s => s.Id == dto.Id)
                    .ExecuteAffrows();
                if (removed == 0)
                    throw new BadRequestException("快捷输入不存在");
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"删除快捷输入失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public async Task ResetAsync(SnippetResetDto dto)
        {
            if (!IsDataScopeAll)
                throw new ForbiddenException("重置快捷输入仅数据范围为全部的用户可操作");
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new BadRequestException("请输入密码");

            // 验证密码：校验当前登录用户的 SysUser 密码（PasswordHasher.Verify）
            try
            {
                var user = await _configDb.Select<SysUserEntity>()
                    .Where(u => u.Id == _currentUser.UserId)
                    .FirstAsync();
                if (user == null || !PasswordHasher.Verify(dto.Password, user.Password))
                    throw new BadRequestException("密码错误");
            }
            catch (BadRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"校验密码失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }

            try
            {
                // 清空现有数据并重新插入初始快捷输入
                _configDb.Delete<SysSqlSnippetEntity>().Where("1=1").ExecuteAffrows();

                var initSnippets = new[]
                {
                    new SysSqlSnippetEntity { Shortcut = "sf",  Expansion = "SELECT * FROM ",         Remark = "查询全部字段", SortOrder = 1, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "sc",  Expansion = "SELECT COUNT(*) FROM ",  Remark = "查询总数",     SortOrder = 2, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "st",  Expansion = "SELECT TOP 100 * FROM ", Remark = "查询前100条", SortOrder = 3, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "wh",  Expansion = "WHERE 1=1",             Remark = "条件子句",     SortOrder = 4, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "ob",  Expansion = "ORDER BY ",             Remark = "排序",         SortOrder = 5, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "gb",  Expansion = "GROUP BY ",             Remark = "分组",         SortOrder = 6, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "ij",  Expansion = "INNER JOIN ",            Remark = "内连接",       SortOrder = 7, CreatedById = _currentUser.UserId },
                    new SysSqlSnippetEntity { Shortcut = "lj",  Expansion = "LEFT JOIN ",             Remark = "左连接",       SortOrder = 8, CreatedById = _currentUser.UserId },
                };
                _configDb.Insert(initSnippets).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                throw new BizException($"重置失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }
    }
}
