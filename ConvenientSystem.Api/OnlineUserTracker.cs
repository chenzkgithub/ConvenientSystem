using System.Collections.Concurrent;

namespace ConvenientSystem.Api
{
    /// <summary>
    /// 在线用户追踪器（单例）：以心跳 CheckStatus 调用为基础，记录已登录用户。
    /// 登录即在线，退出登录即离线；同一账号多次登录以最新记录覆盖。
    /// LastHeartbeat 反映心跳时间（页面开着就更新），LastActive 反映真实操作时间。
    /// </summary>
    public class OnlineUserTracker
    {
        private readonly ConcurrentDictionary<Guid, OnlineEntry> _sessions = new();

        public sealed record OnlineEntry(
            Guid UserId,
            string Account,
            string? DisplayName,
            string Ip,
            DateTime LoginTime,
            DateTime LastHeartbeat,
            DateTime LastActive);

        /// <summary>更新（或新增）用户的在线记录。lastActiveAt 为前端传来的真实操作时间。</summary>
        public void Track(Guid userId, string account, string? displayName, string ip, DateTime? lastActiveAt = null)
        {
            _sessions.AddOrUpdate(
                userId,
                _ => new OnlineEntry(userId, account, displayName, ip, DateTime.Now, DateTime.Now, lastActiveAt ?? DateTime.Now),
                (_, old) =>
                {
                    var newActive = lastActiveAt.HasValue && lastActiveAt.Value > old.LastActive
                        ? lastActiveAt.Value
                        : old.LastActive;
                    return old with { LastHeartbeat = DateTime.Now, LastActive = newActive, Ip = ip };
                });
        }

        /// <summary>用户注销时主动移除。</summary>
        public void Remove(Guid userId) => _sessions.TryRemove(userId, out _);

        /// <summary>返回所有已登录且未注销的用户，按最后活跃时间倒序。</summary>
        public IList<OnlineEntry> GetOnline()
            => [.. _sessions.Values
                .OrderByDescending(e => e.LastActive)];

        /// <summary>清理心跳超过指定分钟数的记录（用于服务重启后或僵尸会话清理）。</summary>
        public void CleanupStale(int staleMinutes)
        {
            var cutoff = DateTime.Now.AddMinutes(-staleMinutes);
            foreach (var kv in _sessions)
            {
                if (kv.Value.LastHeartbeat < cutoff)
                    _sessions.TryRemove(kv.Key, out _);
            }
        }
    }
}
