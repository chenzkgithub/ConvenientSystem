using System.Collections.Concurrent;
using ConvenientSystem.Shared.Common.Security;

namespace ConvenientSystem.Api
{
    /// <summary>
    /// 会话令牌存储（单例）：按"用户 + 客户端平台"记录当前有效 JWT 的 JTI（JWT ID）。
    /// 同一账号在同一平台新登录时覆盖该平台旧 JTI（如新手机登录踢掉旧手机）；
    /// 不同平台（手机端 app / Web 端 web）各自独立会话，跨端登录互不挤号。
    /// 服务重启后内存清空，所有旧令牌在无记录时放行（向后兼容），下次登录重新注册。
    /// </summary>
    public class SessionTokenStore
    {
        private readonly ConcurrentDictionary<(Guid UserId, string Platform), string> _currentJti = new();

        /// <summary>登记该用户指定平台当前有效令牌的 JTI（登录时调用，覆盖该平台旧值）。</summary>
        public void Set(Guid userId, string? platform, string jti)
        {
            var key = (userId, ClientPlatform.Normalize(platform));
            _currentJti.AddOrUpdate(key, jti, (_, _) => jti);
        }

        /// <summary>
        /// 校验请求中的 JTI 是否与该用户该平台当前有效令牌一致。
        /// 以下情况视为通过（向后兼容）：
        /// - 请求无 JTI（升级前的旧令牌）
        /// - 该用户该平台无记录（服务重启后或首次登录）
        /// </summary>
        public bool IsValid(Guid userId, string? platform, string? jti)
        {
            if (string.IsNullOrEmpty(jti)) return true;
            var key = (userId, ClientPlatform.Normalize(platform));
            if (!_currentJti.TryGetValue(key, out var stored)) return true;
            return stored == jti;
        }

        /// <summary>用户主动登出时移除该平台记录（其他平台会话不受影响，可选，当前前端仅清客户端态）。</summary>
        public void Remove(Guid userId, string? platform)
            => _currentJti.TryRemove((userId, ClientPlatform.Normalize(platform)), out _);
    }
}
