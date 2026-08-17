using System.Threading.Channels;
using ConvenientSystem.Shared.Entity.Common;

namespace ConvenientSystem.Shared.Common.Audit
{
    /// <summary>
    /// 审计日志内存队列：单例。中间件把待写日志投递进来，后台服务异步批量落库，
    /// 避免审计写库拖慢主请求。队列有界，满时丢弃最旧的（审计非关键路径，宁丢不阻塞）。
    /// </summary>
    public class AuditLogQueue
    {
        private readonly Channel<SysAuditLogEntity> _channel;

        public AuditLogQueue()
        {
            _channel = Channel.CreateBounded<SysAuditLogEntity>(new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        }

        /// <summary>投递一条审计日志（非阻塞，队列满则丢弃最旧）。</summary>
        public void Enqueue(SysAuditLogEntity log) => _channel.Writer.TryWrite(log);

        /// <summary>供后台服务读取。</summary>
        public ChannelReader<SysAuditLogEntity> Reader => _channel.Reader;
    }
}
