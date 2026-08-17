using System.Security.Claims;

namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// 当前登录用户信息：从 HttpContext 的 JWT 中提取，供 Service 层做数据权限隔离。
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>当前用户 Id（GUID）；未登录时为 null。</summary>
        Guid? UserId { get; }

        /// <summary>当前用户账号；未登录时为 null。</summary>
        string? Account { get; }

        /// <summary>当前用户是否为管理员（可看所有数据）。</summary>
        bool IsAdmin { get; }

        /// <summary>当前用户的数据范围（多个角色冲突时取最宽松的）。</summary>
        DataScope DataScope { get; }
    }
}
