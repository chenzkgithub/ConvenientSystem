using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.Data.SqlClient;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 查询收藏管理服务实现：配置存储在本地配置库 SysSqlFavorite 表。
    /// </summary>
    public class SqlFavoriteService : ISqlFavoriteService
    {
        private readonly IFreeSql _configDb;
        private readonly ICurrentUser _currentUser;

        public SqlFavoriteService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            ICurrentUser currentUser)
        {
            _configDb = configDb;
            _currentUser = currentUser;
        }

        private bool IsDataScopeAll => _currentUser.DataScope == DataScope.All;
        private bool IsOwner(Guid? createdById)
            => _currentUser.UserId.HasValue && createdById == _currentUser.UserId;
        private void EnsureOwner(SysSqlFavoriteEntity entity)
        {
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new ForbiddenException("无权操作该收藏");
        }

        public List<SqlFavoriteDto> GetList()
        {
            try
            {
                var query = _configDb.Select<SysSqlFavoriteEntity>()
                    .OrderBy(s => s.SortOrder)
                    .OrderBy(s => s.Id);
                if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                    query = query.Where(s => s.CreatedById == _currentUser.UserId);
                return query.ToList(s => new SqlFavoriteDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    SqlContent = s.SqlContent,
                    Remark = s.Remark,
                    DataSource = s.DataSource,
                    SortOrder = s.SortOrder
                });
            }
            catch (Exception ex)
            {
                throw new BizException($"读取 SQL 收藏失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Add(SqlFavoriteDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("收藏名称不能为空");
            if (string.IsNullOrWhiteSpace(dto.SqlContent))
                throw new BadRequestException("SQL 内容不能为空");
            try
            {
                _configDb.Insert(new SysSqlFavoriteEntity
                {
                    Name = dto.Name.Trim(),
                    SqlContent = dto.SqlContent,
                    Remark = dto.Remark?.Trim(),
                    DataSource = dto.DataSource?.Trim(),
                    SortOrder = dto.SortOrder,
                    CreatedById = _currentUser.UserId
                }).ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"添加 SQL 收藏失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Update(SqlFavoriteDto dto)
        {
            if (dto.Id <= 0)
                throw new BadRequestException("无效的收藏 Id");
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("收藏名称不能为空");
            try
            {
                var entity = _configDb.Select<SysSqlFavoriteEntity>()
                    .Where(s => s.Id == dto.Id)
                    .First()
                    ?? throw new NotFoundException("收藏不存在");
                EnsureOwner(entity);
                entity.Name = dto.Name.Trim();
                entity.SqlContent = dto.SqlContent;
                entity.Remark = dto.Remark?.Trim();
                entity.DataSource = dto.DataSource?.Trim();
                entity.SortOrder = dto.SortOrder;
                _configDb.Update<SysSqlFavoriteEntity>()
                    .SetSource(entity)
                    .ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"修改 SQL 收藏失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Remove(SqlFavoriteDto dto)
        {
            if (dto.Id <= 0)
                throw new BadRequestException("无效的收藏 Id");
            try
            {
                var entity = _configDb.Select<SysSqlFavoriteEntity>()
                    .Where(s => s.Id == dto.Id)
                    .First()
                    ?? throw new NotFoundException("收藏不存在");
                EnsureOwner(entity);
                _configDb.Delete<SysSqlFavoriteEntity>()
                    .Where(s => s.Id == dto.Id)
                    .ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"删除 SQL 收藏失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }
    }
}
