using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 好友业务服务：搜索（按发起方平台过滤）/ 申请 / 处理 / 删除；
    /// 好友列表复用 IChatService.GetContacts（通讯录=好友）。
    /// 实时推送（SignalR）由 Api 层 FriendController 结合 IHubContext 编排：新申请推 FriendRequest 给接收方、
    /// 处理结果推 FriendRequestHandled 给申请人。
    /// </summary>
    public interface IFriendService
    {
        /// <summary>
        /// 搜索可添加的用户：账号/姓名模糊匹配，排除自己/已有好友/双向屏蔽；
        /// 按发起方平台过滤（app=对方须开通 app-friends+app-login；web=对方须有任意启用角色；管理员恒可被搜到）。
        /// </summary>
        List<FriendSearchResultDto> SearchUsers(Guid userId, string keyword, string platform);

        /// <summary>发起好友申请：校验非自己/对方启用/非好友/无待处理申请/无屏蔽/平台可加；返回申请条目（供推送）。</summary>
        FriendRequestDto SendRequest(Guid fromUserId, Guid toUserId, string? message, string platform);

        /// <summary>我收到的好友申请（全部状态，最新在前，前端自行分组待处理/已处理）。</summary>
        List<FriendRequestDto> GetRequests(Guid userId);

        /// <summary>待处理好友申请数（入口角标轮询）。</summary>
        int GetPendingCount(Guid userId);

        /// <summary>处理好友申请（仅接收方可处理待处理申请）：同意则双方互加好友。返回申请人 Id 与处理结果（供推送）。</summary>
        (Guid FromUserId, FriendRequestHandledDto Result) HandleRequest(Guid userId, long requestId, bool accept);

        /// <summary>删除好友（成对删两行，幂等；已有会话与历史消息保留，但双方不能再互发消息）。</summary>
        void DeleteFriend(Guid userId, Guid friendId);

        /// <summary>设置/清除好友备注：仅更新我方持有行（双行模式互不影响）；空串=清除备注。</summary>
        void SetRemark(Guid userId, Guid friendUserId, string? remark);
    }
}
