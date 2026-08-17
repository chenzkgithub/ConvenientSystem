using System.Collections.Concurrent;

namespace ConvenientSystem.Api
{
    /// <summary>
    /// 会话令牌存储（单例）：记录每个用户当前有效 JWT 的 JTI（JWT ID）。
    /// 同一账号新登录时覆盖旧 JTI，旧令牌在中间件校验时会被拒绝，实现"挤号"效果。
    /// 服务重启后内存清空，所有旧令牌在无记录时放行（向后兼容），下次登录重新注册。
    /// </summary>
    public class SessionTokenStore
    {
        private readonly ConcurrentDictionary<Guid, string> _currentJti = new();

        /// <summary>登记该用户当前有效令牌的 JTI（新登录时调用，覆盖旧值）。</summary>
        public void Set(Guid userId, string jti) => _currentJti.AddOrUpdate(userId, jti, (_, _) => jti);

        /// <summary>
        /// 校验请求中的 JTI 是否与该用户当前有效令牌一致。
        /// 以下情况视为通过（向后兼容）：
        /// - 请求无 JTI（升级前的旧令牌）
        /// - 该用户无记录（服务重启后或首次登录）
        /// </summary>
        public bool IsValid(Guid userId, string? jti)
        {
            if (string.IsNullOrEmpty(jti)) return true;
            if (!_currentJti.TryGetValue(userId, out var stored)) return true;
            return stored == jti;
        }

        /// <summary>用户主动登出时移除记录（可选，当前前端仅清客户端态）。</summary>
        public void Remove(Guid userId) => _currentJti.TryRemove(userId, out _);
    }
}
