using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统通知业务服务：管理端发布/编辑/删除 + 用户端查看/已读。
    /// 管理端操作由控制器挂 notice 权限码保护；用户端操作只要求已登录。
    /// </summary>
    public interface INoticeService
    {
        /// <summary>管理端：全部通知列表（含发布人信息，按发布时间倒序）。</summary>
        List<NoticeDto> GetList();

        /// <summary>管理端：新增（新建时按开关触发联动推送）或编辑通知。</summary>
        void Save(NoticeDto dto);

        /// <summary>管理端：删除通知及其已读记录。</summary>
        void Delete(int id);

        /// <summary>用户端：启用的通知列表（含当前用户已读状态，按发布时间倒序）。</summary>
        List<NoticeUserDto> GetMyList(Guid userId);

        /// <summary>用户端：当前用户未读通知数。</summary>
        int GetUnreadCount(Guid userId);

        /// <summary>用户端：标记单条通知已读（幂等）。</summary>
        void MarkRead(Guid userId, int noticeId);

        /// <summary>用户端：全部未读通知标记已读。</summary>
        void MarkAllRead(Guid userId);
    }
}
