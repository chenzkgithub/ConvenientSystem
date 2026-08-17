namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 通知管理 DTO：管理端列表展示与保存共用（Id=0 表示新建）。
    /// </summary>
    public class NoticeDto
    {
        /// <summary>通知 Id（0 表示新建）</summary>
        public int Id { get; set; }

        /// <summary>通知标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>通知内容</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>通知级别：1=普通 2=重要 3=紧急</summary>
        public byte Level { get; set; } = 1;

        /// <summary>发布时联动邮件推送</summary>
        public bool SendEmail { get; set; }

        /// <summary>发布时联动短信推送</summary>
        public bool SendSms { get; set; }

        /// <summary>发布时联动群机器人广播</summary>
        public bool SendWebhook { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>有效期截止时间（NULL=永久有效；过期后用户端不再展示）</summary>
        public DateTime? ExpireTime { get; set; }

        /// <summary>发布人账号（列表展示用）</summary>
        public string? CreatedByAccount { get; set; }

        /// <summary>发布人姓名（列表展示用）</summary>
        public string? CreatedByName { get; set; }

        /// <summary>发布时间（列表返回用；保存时忽略——新建由数据库默认值生成，可空避免客户端传空值反序列化失败）</summary>
        public DateTime? CreateTime { get; set; }

        /// <summary>定向接收用户 Id 列表（与角色列表均为空时默认发送给全部人员）</summary>
        public List<Guid> TargetUserIds { get; set; } = new();

        /// <summary>定向接收角色 Id 列表（与用户列表均为空时默认发送给全部人员）</summary>
        public List<int> TargetRoleIds { get; set; } = new();

        /// <summary>定向用户数（列表展示用，0 且无定向角色表示全员）</summary>
        public int TargetUserCount { get; set; }

        /// <summary>定向角色数（列表展示用）</summary>
        public int TargetRoleCount { get; set; }
    }

    /// <summary>
    /// 用户端通知 DTO：仅展示启用的通知，附带当前用户已读状态。
    /// </summary>
    public class NoticeUserDto
    {
        /// <summary>通知 Id</summary>
        public int Id { get; set; }

        /// <summary>通知标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>通知内容</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>通知级别：1=普通 2=重要 3=紧急</summary>
        public byte Level { get; set; }

        /// <summary>发布时间</summary>
        public DateTime CreateTime { get; set; }

        /// <summary>当前用户是否已读</summary>
        public bool IsRead { get; set; }
    }

    /// <summary>未读数查询结果</summary>
    public class NoticeUnreadDto
    {
        /// <summary>未读通知数</summary>
        public int Count { get; set; }
    }
}
