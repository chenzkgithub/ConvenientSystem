using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// JWT 签发与校验参数构建：对称密钥（HMAC-SHA256），密钥由调用方从配置/环境变量提供。
    /// 载荷含 userId / account / displayName、角色（ClaimTypes.Role）、菜单权限码（menuCodes，逗号拼接）。
    /// </summary>
    public static class JwtHelper
    {
        public const string Issuer = "ConvenientSystem";
        public const string Audience = "ConvenientSystem";
        /// <summary>菜单权限码 claim 名（值为逗号拼接的菜单 Name，供接口鉴权比对）。</summary>
        public const string MenuCodesClaim = "menuCodes";
        public const string UserIdClaim = "userId";
        public const string AccountClaim = "account";
        public const string DisplayNameClaim = "displayName";
        /// <summary>管理员标记 claim（值为 true/false）。</summary>
        public const string AdminClaim = "isAdmin";
        /// <summary>数据范围 claim（值为 DataScope 整数值）。</summary>
        public const string DataScopeClaim = "dataScope";
        /// <summary>超级管理员角色编码：拥有全部菜单与接口权限。</summary>
        public const string AdminRole = "admin";
        /// <summary>普通用户角色编码：新注册用户自动赋予。</summary>
        public const string UserRole = "user";

        /// <summary>由字符串密钥构建对称签名密钥（不足 32 字节右侧补 0，满足 HMAC-SHA256 长度要求）。</summary>
        public static SymmetricSecurityKey BuildKey(string key)
            => new(Encoding.UTF8.GetBytes((key ?? string.Empty).PadRight(32, '0')));

        /// <summary>签发 JWT（默认有效期 30 天，与前端"登录态长期保留"一致）。</summary>
        public static string GenerateToken(
            string key,
            Guid userId,
            string account,
            string? displayName,
            IEnumerable<string> roleCodes,
            IEnumerable<string> menuCodes,
            TimeSpan? lifetime = null,
            bool isAdmin = false,
            DataScope dataScope = DataScope.Self)
        {
            var claims = new List<Claim>
            {
                new(UserIdClaim, userId.ToString()),
                new(AccountClaim, account),
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, account),
                new(MenuCodesClaim, string.Join(',', menuCodes.Distinct())),
                new(AdminClaim, isAdmin ? "true" : "false"),
                new(DataScopeClaim, ((int)dataScope).ToString()),
                // JWT ID：唯一标识本次签发的令牌，用于挤号（同账号新登录覆盖旧 JTI，旧令牌被中间件拒绝）
                new("jti", Guid.NewGuid().ToString("N")),
            };
            if (!string.IsNullOrEmpty(displayName))
                claims.Add(new Claim(DisplayNameClaim, displayName));
            foreach (var role in roleCodes.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
                claims.Add(new Claim(ClaimTypes.Role, role));

            var creds = new SigningCredentials(BuildKey(key), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>构建 JWT 校验参数（供 Api 的 JwtBearer 中间件使用）。</summary>
        public static TokenValidationParameters BuildValidationParameters(string key) => new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = BuildKey(key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
        };
    }

    /// <summary>
    /// JWT 密钥持有者（单例）：API 启动时一次性确定密钥，签发方（LoginService）与验证方
    /// （TokenValidationParameters）共用同一实例，避免启动时 DB 不可用导致 ReadJwtKeyFromDb
    /// 回退默认值、而 LoginService 从 DB 读到不同值时密钥不一致——不一致会令签发的 token
    /// 无法通过验证，后续请求全部 401，用户登录后立即被踢出。
    /// </summary>
    public class JwtKeyHolder
    {
        /// <summary>API 启动时确定的 JWT 对称密钥（环境变量 → SysConfig 表 → 内置缺省）。</summary>
        public string Key { get; }

        public JwtKeyHolder(string key) => Key = key;
    }
}
