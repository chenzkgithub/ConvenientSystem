using System.Collections.Concurrent;

namespace ConvenientSystem.Api
{
    /// <summary>
    /// 在线用户追踪器（单例）：以心跳 CheckStatus 调用为基础，记录最近活跃的已登录用户。
    /// 同一账号多次登录以最新活跃时间覆盖；超过 <see cref="TimeoutMinutes"/> 分钟未活跃自动视为离线。
    /// </summary>
    public class OnlineUserTracker
    {
        /// <summary>超过此时间无心跳则视为离线。</summary>
        public const int TimeoutMinutes = 6;

        private readonly ConcurrentDictionary<Guid, OnlineEntry> _sessions = new();

        public sealed record OnlineEntry(
            Guid UserId,
            string Account,
            string? DisplayName,
            string Ip,
            DateTime LoginTime,
            DateTime LastSeen);

        /// <summary>更新（或新增）用户的在线活跃记录。</summary>
        public void Track(Guid userId, string account, string? displayName, string ip)
        {
            _sessions.AddOrUpdate(
                userId,
                _ => new OnlineEntry(userId, account, displayName, ip, DateTime.Now, DateTime.Now),
                (_, old) => old with { LastSeen = DateTime.Now, Ip = ip });
        }

        /// <summary>用户注销时主动移除。</summary>
        public void Remove(Guid userId) => _sessions.TryRemove(userId, out _);

        /// <summary>返回最近 <see cref="TimeoutMinutes"/> 分钟内有心跳的在线用户，按最后活跃时间倒序。</summary>
        public IList<OnlineEntry> GetOnline()
            => [.. _sessions.Values
                .Where(e => e.LastSeen >= DateTime.Now.AddMinutes(-TimeoutMinutes))
                .OrderByDescending(e => e.LastSeen)];
    }
}
