using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 机器人发送日志表（见 db/init.sql dbo.SysWebhookLog）。
    /// 每次 SendOneAsync 发送后记录一条（群/私聊各记一条）。
    /// </summary>
    [Table(Name = "SysWebhookLog")]
    public class SysWebhookLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联配置 Id</summary>
        public int ConfigId { get; set; }

        /// <summary>配置名称（冗余存储，便于日志展示）</summary>
        public string ConfigName { get; set; } = string.Empty;

        /// <summary>服务商类型：dingtalk / wecom / feishu</summary>
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>消息标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>消息内容（截断 2000 字符）</summary>
        [Column(StringLength = -1)]
        public string Content { get; set; } = string.Empty;

        /// <summary>是否发送成功</summary>
        public bool Success { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>耗时毫秒</summary>
        public int CostMs { get; set; }

        /// <summary>发送时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}
