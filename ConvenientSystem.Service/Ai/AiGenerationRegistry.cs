using System.Collections.Concurrent;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// 生成中任务注册表（Singleton）：assistantMessageId → CancellationTokenSource。
    /// Send 后台生成前 Begin 登记、终态后 End 注销；StopGeneration 按 Id 取消。
    /// 进程重启后注册表清空，但消息 Status 恒为 0 的残留由前端「生成中超 2 分钟视为失败」兜底（P0 简化，不做启动修复）。
    /// </summary>
    public class AiGenerationRegistry
    {
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _sources = new();

        /// <summary>登记一次生成，返回供生成循环使用的 CTS</summary>
        public CancellationTokenSource Begin(long messageId)
        {
            End(messageId);
            var cts = new CancellationTokenSource();
            _sources[messageId] = cts;
            return cts;
        }

        /// <summary>请求取消（存在且未完成时返回 true）</summary>
        public bool TryCancel(long messageId)
        {
            if (_sources.TryGetValue(messageId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }
            return false;
        }

        /// <summary>生成结束注销（幂等）</summary>
        public void End(long messageId)
        {
            if (_sources.TryRemove(messageId, out var cts))
                cts.Dispose();
        }
    }
}
