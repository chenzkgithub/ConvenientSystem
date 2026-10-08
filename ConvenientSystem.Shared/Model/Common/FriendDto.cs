namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>好友搜索结果条目：已按发起方平台过滤（对方在发起方所在平台可被添加），并排除自己/已有好友/双向屏蔽。</summary>
    public class FriendSearchResultDto
    {
        public Guid UserId { get; set; }

        public string Account { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Avatar { get; set; }
    }

    /// <summary>发起好友申请请求。</summary>
    public class FriendSendRequest
    {
        /// <summary>目标用户 Id。</summary>
        public Guid ToUserId { get; set; }

        /// <summary>验证留言（可选，最长 100 字）。</summary>
        public string? Message { get; set; }
    }

    /// <summary>处理好友申请请求：只能处理发给自己的待处理申请。</summary>
    public class FriendHandleRequest
    {
        public long RequestId { get; set; }

        /// <summary>true=同意（双方互加好友）；false=拒绝。</summary>
        public bool Accept { get; set; }
    }

    /// <summary>好友申请条目（接收方视角，含申请人信息；历史与待处理一并返回，前端自行分组）。</summary>
    public class FriendRequestDto
    {
        public long Id { get; set; }

        /// <summary>申请人 Id。</summary>
        public Guid FromUserId { get; set; }

        public string FromAccount { get; set; } = string.Empty;

        public string? FromDisplayName { get; set; }

        public string? FromAvatar { get; set; }

        /// <summary>验证留言。</summary>
        public string? Message { get; set; }

        /// <summary>0=待处理 1=已同意 2=已拒绝。</summary>
        public int Status { get; set; }

        public DateTime CreateTime { get; set; }
    }

    /// <summary>好友申请处理结果（SignalR FriendRequestHandled 事件体，推给申请人）。</summary>
    public class FriendRequestHandledDto
    {
        public long RequestId { get; set; }

        /// <summary>true=已同意（双方已互为好友）。</summary>
        public bool Accept { get; set; }

        /// <summary>处理人显示名（前端提示「张三同意了你的好友申请」）。</summary>
        public string? HandlerName { get; set; }
    }

    /// <summary>待处理好友申请数（入口角标轮询）。</summary>
    public class FriendRequestCountDto
    {
        public int Count { get; set; }
    }

    /// <summary>设置好友备注请求：仅更新我方持有行的备注（双行模式互不影响）。</summary>
    public class FriendRemarkSaveRequest
    {
        /// <summary>好友用户 Id。</summary>
        public Guid FriendUserId { get; set; }

        /// <summary>备注名（空串=清除备注；最长 100 字）。</summary>
        public string? Remark { get; set; }
    }
}
