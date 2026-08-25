using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统通知业务服务实现：管理端发布/编辑/删除 + 用户端查看/已读。
    /// 新建发布时按勾选开关注入 NoticePushJob 联动邮件/短信/群机器人推送；编辑不重复推送。
    /// </summary>
    public class NoticeService : INoticeService
    {
        /// <summary>用户端通知列表上限（通知为低频数据，全量返回足以覆盖铃铛弹层展示）</summary>
        private const int UserListLimit = 200;

        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public NoticeService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        /// <summary>管理端：全部通知列表（含发布人账号/姓名与定向范围，按发布时间倒序）。</summary>
        public List<NoticeDto> GetList()
        {
            var notices = _fsql.Select<SysNoticeEntity>()
                .OrderByDescending(n => n.CreateTime)
                .ToList();

            var userMap = UserDisplayHelper.GetMap(_fsql, notices.Select(n => n.CreatedById));

            // 定向范围：一次性查出全部通知的定向用户/角色记录，避免逐条查询
            var ids = notices.Select(n => n.Id).ToList();
            var userTargets = ids.Count == 0 ? new List<SysNoticeUserEntity>()
                : _fsql.Select<SysNoticeUserEntity>().Where(t => ids.Contains(t.NoticeId)).ToList();
            var roleTargets = ids.Count == 0 ? new List<SysNoticeRoleEntity>()
                : _fsql.Select<SysNoticeRoleEntity>().Where(t => ids.Contains(t.NoticeId)).ToList();

            return notices.Select(n =>
            {
                var display = UserDisplayHelper.Find(userMap, n.CreatedById);
                var targetUserIds = userTargets.Where(t => t.NoticeId == n.Id).Select(t => t.UserId).ToList();
                var targetRoleIds = roleTargets.Where(t => t.NoticeId == n.Id).Select(t => t.RoleId).ToList();
                return new NoticeDto
                {
                    Id = n.Id,
                    Title = n.Title,
                    Content = n.Content,
                    Level = n.Level,
                    SendEmail = n.SendEmail,
                    SendSms = n.SendSms,
                    SendWebhook = n.SendWebhook,
                    Enabled = n.Enabled,
                    ExpireTime = n.ExpireTime,
                    CreatedByAccount = display?.Account,
                    CreatedByName = display?.DisplayName,
                    CreateTime = n.CreateTime,
                    TargetUserIds = targetUserIds,
                    TargetRoleIds = targetRoleIds,
                    TargetUserCount = targetUserIds.Count,
                    TargetRoleCount = targetRoleIds.Count
                };
            }).ToList();
        }

        /// <summary>系统内部：创建一条不触发邮件/短信/群机器人推送的全员可见通知。</summary>
        public int CreateSystemNotice(string title, string content, byte level = 1, DateTime? expireTime = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("通知标题不能为空", nameof(title));
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("通知内容不能为空", nameof(content));

            var id = _fsql.Insert(new SysNoticeEntity
            {
                Title = title.Trim(),
                Content = content.Trim(),
                Level = level is >= 1 and <= 3 ? level : (byte)1,
                SendEmail = false,
                SendSms = false,
                SendWebhook = false,
                Enabled = true,
                ExpireTime = expireTime,
                CreatedById = null, // 系统通知：对所有用户可见，不会被"跳过发布人"过滤
                UpdateTime = DateTime.Now
            }).ExecuteIdentity();

            return (int)id;
        }

        /// <summary>管理端：新增（触发联动推送）或编辑通知。</summary>
        public void Save(NoticeDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new BadRequestException("通知标题不能为空");
            if (string.IsNullOrWhiteSpace(dto.Content))
                throw new BadRequestException("通知内容不能为空");
            if (dto.ExpireTime.HasValue && dto.ExpireTime.Value <= DateTime.Now)
                throw new BadRequestException("有效期必须晚于当前时间");

            var title = dto.Title.Trim();
            var content = dto.Content.Trim();
            var level = dto.Level is >= 1 and <= 3 ? dto.Level : (byte)1;
            var expireTime = dto.ExpireTime;

            if (dto.Id == 0)
            {
                var id = _fsql.Insert(new SysNoticeEntity
                {
                    Title = title,
                    Content = content,
                    Level = level,
                    SendEmail = dto.SendEmail,
                    SendSms = dto.SendSms,
                    SendWebhook = dto.SendWebhook,
                    Enabled = dto.Enabled,
                    ExpireTime = expireTime,
                    CreatedById = _currentUser.UserId,
                    UpdateTime = DateTime.Now
                }).ExecuteIdentity();

                // 保存定向范围（新建与编辑一致：先清后写，空列表即全员）
                SaveTargets((int)id, dto);

                // 仅新建发布时联动推送；编辑不重复推送
                if (dto.SendEmail || dto.SendSms || dto.SendWebhook)
                    BackgroundJob.Enqueue<NoticePushJob>(j => j.PushAsync((int)id));
            }
            else
            {
                var affected = _fsql.Update<SysNoticeEntity>()
                    .Set(n => n.Title, title)
                    .Set(n => n.Content, content)
                    .Set(n => n.Level, level)
                    .Set(n => n.SendEmail, dto.SendEmail)
                    .Set(n => n.SendSms, dto.SendSms)
                    .Set(n => n.SendWebhook, dto.SendWebhook)
                    .Set(n => n.Enabled, dto.Enabled)
                    .Set(n => n.ExpireTime, expireTime)
                    .Set(n => n.UpdateTime, DateTime.Now)
                    .Where(n => n.Id == dto.Id)
                    .ExecuteAffrows();
                if (affected == 0)
                    throw new NotFoundException("通知不存在");

                // 同步定向范围
                SaveTargets(dto.Id, dto);
            }
        }

        /// <summary>管理端：删除通知及其已读记录与定向范围。</summary>
        public void Delete(int id)
        {
            _fsql.Delete<SysNoticeReadEntity>().Where(r => r.NoticeId == id).ExecuteAffrows();
            _fsql.Delete<SysNoticeUserEntity>().Where(t => t.NoticeId == id).ExecuteAffrows();
            _fsql.Delete<SysNoticeRoleEntity>().Where(t => t.NoticeId == id).ExecuteAffrows();
            var affected = _fsql.Delete<SysNoticeEntity>().Where(n => n.Id == id).ExecuteAffrows();
            if (affected == 0)
                throw new NotFoundException("通知不存在");
        }

        /// <summary>保存通知定向范围：先清除旧记录再写入；用户与角色均为空表示发送给全部人员。</summary>
        private void SaveTargets(int noticeId, NoticeDto dto)
        {
            _fsql.Delete<SysNoticeUserEntity>().Where(t => t.NoticeId == noticeId).ExecuteAffrows();
            _fsql.Delete<SysNoticeRoleEntity>().Where(t => t.NoticeId == noticeId).ExecuteAffrows();

            var users = (dto.TargetUserIds ?? new List<Guid>()).Distinct()
                .Select(uid => new SysNoticeUserEntity { NoticeId = noticeId, UserId = uid }).ToList();
            if (users.Count > 0)
                _fsql.Insert(users).ExecuteAffrows();

            var roles = (dto.TargetRoleIds ?? new List<int>()).Distinct()
                .Select(rid => new SysNoticeRoleEntity { NoticeId = noticeId, RoleId = rid }).ToList();
            if (roles.Count > 0)
                _fsql.Insert(roles).ExecuteAffrows();
        }

        /// <summary>
        /// 计算指定通知中用户可见的 Id 集合：
        /// 某通知在定向用户表与定向角色表中均无记录时全员可见；
        /// 否则命中定向用户本人或所属角色之一即可见（两者取并集）。
        /// </summary>
        private HashSet<int> FilterVisibleNoticeIds(Guid userId, List<int> noticeIds)
        {
            if (noticeIds.Count == 0) return new HashSet<int>();

            var userTargets = _fsql.Select<SysNoticeUserEntity>()
                .Where(t => noticeIds.Contains(t.NoticeId)).ToList();
            var roleTargets = _fsql.Select<SysNoticeRoleEntity>()
                .Where(t => noticeIds.Contains(t.NoticeId)).ToList();

            var targetedIds = userTargets.Select(t => t.NoticeId)
                .Concat(roleTargets.Select(t => t.NoticeId)).ToHashSet();
            if (targetedIds.Count == 0) return noticeIds.ToHashSet(); // 全部通知均未定向：全员可见

            var userRoleIds = _fsql.Select<SysUserRoleEntity>()
                .Where(r => r.UserId == userId)
                .ToList(r => r.RoleId).ToHashSet();

            return noticeIds.Where(id =>
                !targetedIds.Contains(id)
                || userTargets.Any(t => t.NoticeId == id && t.UserId == userId)
                || roleTargets.Any(t => t.NoticeId == id && userRoleIds.Contains(t.RoleId))
            ).ToHashSet();
        }

        /// <summary>用户端：当前用户可见的启用且未过期通知列表（含已读状态，按发布时间倒序；跳过发布人本人）。</summary>
        public List<NoticeUserDto> GetMyList(Guid userId)
        {
            var now = DateTime.Now;
            var notices = _fsql.Select<SysNoticeEntity>()
                .Where(n => n.Enabled && n.CreatedById != userId)
                .Where(n => n.ExpireTime == null || n.ExpireTime > now)
                .OrderByDescending(n => n.CreateTime)
                .Take(UserListLimit)
                .ToList();
            if (notices.Count == 0) return new List<NoticeUserDto>();

            // 定向范围过滤：未定向的通知全员可见，定向通知仅命中用户/角色可见
            var visibleIds = FilterVisibleNoticeIds(userId, notices.Select(n => n.Id).ToList());
            notices = notices.Where(n => visibleIds.Contains(n.Id)).ToList();
            if (notices.Count == 0) return new List<NoticeUserDto>();

            var ids = notices.Select(n => n.Id).ToList();
            var readIds = _fsql.Select<SysNoticeReadEntity>()
                .Where(r => r.UserId == userId && ids.Contains(r.NoticeId))
                .ToList(r => r.NoticeId)
                .ToHashSet();

            return notices.Select(n => new NoticeUserDto
            {
                Id = n.Id,
                Title = n.Title,
                Content = n.Content,
                Level = n.Level,
                CreateTime = n.CreateTime,
                IsRead = readIds.Contains(n.Id)
            }).ToList();
        }

        /// <summary>用户端：当前用户未读通知数（仅统计对他可见且未过期的通知，跳过发布人本人）。</summary>
        public int GetUnreadCount(Guid userId)
        {
            var now = DateTime.Now;
            var noticeIds = _fsql.Select<SysNoticeEntity>()
                .Where(n => n.Enabled && n.CreatedById != userId)
                .Where(n => n.ExpireTime == null || n.ExpireTime > now)
                .OrderByDescending(n => n.CreateTime)
                .Take(UserListLimit)
                .ToList(n => n.Id);
            if (noticeIds.Count == 0) return 0;

            var visibleIds = FilterVisibleNoticeIds(userId, noticeIds);
            if (visibleIds.Count == 0) return 0;

            var readCount = _fsql.Select<SysNoticeReadEntity>()
                .Where(r => r.UserId == userId && visibleIds.Contains(r.NoticeId))
                .Count();
            return (int)Math.Max(0, visibleIds.Count - readCount);
        }

        /// <summary>用户端：标记单条通知已读（幂等；停用/过期/不可见/不存在的通知不记录）。</summary>
        public void MarkRead(Guid userId, int noticeId)
        {
            var now = DateTime.Now;
            var exists = _fsql.Select<SysNoticeEntity>()
                .Where(n => n.Id == noticeId && n.Enabled)
                .Where(n => n.ExpireTime == null || n.ExpireTime > now)
                .Any();
            if (!exists) return;

            // 定向通知仅允许目标用户标记已读
            var visibleIds = FilterVisibleNoticeIds(userId, new List<int> { noticeId });
            if (!visibleIds.Contains(noticeId)) return;

            var already = _fsql.Select<SysNoticeReadEntity>()
                .Where(r => r.NoticeId == noticeId && r.UserId == userId).Any();
            if (already) return;

            _fsql.Insert(new SysNoticeReadEntity { NoticeId = noticeId, UserId = userId }).ExecuteAffrows();
        }

        /// <summary>用户端：全部未读（启用、未过期、对其可见且非本人发布）通知标记已读。</summary>
        public void MarkAllRead(Guid userId)
        {
            var now = DateTime.Now;
            var noticeIds = _fsql.Select<SysNoticeEntity>()
                .Where(n => n.Enabled && n.CreatedById != userId)
                .Where(n => n.ExpireTime == null || n.ExpireTime > now)
                .Take(UserListLimit)
                .ToList(n => n.Id);
            var visibleIds = FilterVisibleNoticeIds(userId, noticeIds);
            if (visibleIds.Count == 0) return;

            var readIds = _fsql.Select<SysNoticeReadEntity>()
                .Where(r => r.UserId == userId)
                .ToList(r => r.NoticeId)
                .ToHashSet();

            var unreadIds = visibleIds.Where(id => !readIds.Contains(id)).ToList();
            if (unreadIds.Count == 0) return;

            _fsql.Insert(unreadIds.Select(id => new SysNoticeReadEntity { NoticeId = id, UserId = userId }))
                .ExecuteAffrows();
        }
    }
}
