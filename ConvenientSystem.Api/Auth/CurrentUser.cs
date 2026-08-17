using System.Security.Claims;
using ConvenientSystem.Shared.Common.Security;

namespace ConvenientSystem.Api.Auth
{
    /// <summary>
    /// 当前登录用户信息实现：从 HttpContext.User 的 JWT claim 中读取。
    /// 服务注册为 Singleton，依赖 IHttpContextAccessor（Singleton）获取每次请求的 HttpContext。
    /// </summary>
    public class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        public Guid? UserId
        {
            get
            {
                var raw = User?.FindFirst(JwtHelper.UserIdClaim)?.Value
                          ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
            }
        }

        public string? Account => User?.FindFirst(JwtHelper.AccountClaim)?.Value
                                  ?? User?.FindFirst(ClaimTypes.Name)?.Value;

        public bool IsAdmin
        {
            get
            {
                var raw = User?.FindFirst(JwtHelper.AdminClaim)?.Value;
                return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                       || User?.IsInRole(JwtHelper.AdminRole) == true;
            }
        }

        public DataScope DataScope
        {
            get
            {
                var raw = User?.FindFirst(JwtHelper.DataScopeClaim)?.Value;
                return int.TryParse(raw, out var value) && Enum.IsDefined(typeof(DataScope), value)
                    ? (DataScope)value
                    : DataScope.Self;
            }
        }
    }
}
