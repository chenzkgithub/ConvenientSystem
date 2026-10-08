using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 好友业务实现：搜索（按发起方平台过滤）/ 申请 / 处理 / 删除。
    /// 通讯规则（好友才能单聊）的校验在 ChatService（OpenConversation/SendMessage/转发），
    /// 本服务只管好友关系生命周期；屏蔽优先于好友（屏蔽双方无法申请/互加）。
    /// </summary>
    public class FriendService : IFriendService
    {
        /// <summary>搜索结果上限（内部系统规模小，防全表模糊扫）。</summary>
        private const int SearchLimit = 50;

        /// <summary>验证留言长度上限（与 ChatFriendRequest.Message 列宽一致）。</summary>
        private const int MaxMessageLength = 100;

        /// <summary>好友备注长度上限（与 ChatFriendship.Remark 列宽一致）。</summary>
        private const int MaxRemarkLength = 100;

        /// <summary>内置管理员账号（与 AppPermService/UserManageService 保护口径一致）：恒可被搜到、恒可被添加。</summary>
        private const string BuiltInAdminAccount = "admin";

        private readonly ILogger<FriendService> _logger;
        private readonly IFreeSql _fsql;

        public FriendService(
            ILogger<FriendService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _logger = logger;
            _fsql = fsql;
        }

        /// <summary>
        /// 搜索可添加的用户：账号/姓名模糊匹配，排除自己/已有好友/双向屏蔽；
        /// 平台过滤（发起方视角）：app=对方须开通 app-friends+app-login；web=对方须有任意启用角色；管理员恒可被搜到。
        /// </summary>
        public List<FriendSearchResultDto> SearchUsers(Guid userId, string keyword, string platform)
        {
            var kw = (keyword ?? string.Empty).Trim();
            if (kw.Length == 0) return new List<FriendSearchResultDto>();

            // 基础候选：启用 + 未删除 + 非自己 + 账号/姓名匹配（上限 50，账号排序）
            var candidates = _fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled && !u.IsDeleted && u.Id != userId)
                .Where(u => u.Account.Contains(kw) || (u.DisplayName != null && u.DisplayName.Contains(kw)))
                .OrderBy(u => u.Account)
                .Limit(SearchLimit)
                .ToList(u => new { u.Id, u.Account, u.DisplayName, u.Avatar });
            if (candidates.Count == 0) return new List<FriendSearchResultDto>();

            var candidateIds = candidates.Select(c => c.Id).ToList();

            // 已是好友的不再出现在结果（加好友流程只面向陌生人）
            var friendIds = _fsql.Select<ChatFriendshipEntity>()
                .Where(f => f.UserId == userId && candidateIds.Contains(f.FriendId))
                .ToList(f => f.FriendId)
                .ToHashSet();

            // 双向屏蔽的不出现（屏蔽优先于好友：屏蔽关系下无法加好友）
            var blocks = _fsql.Select<ChatBlockListEntity>()
                .Where(b => (b.UserId == userId && candidateIds.Contains(b.BlockedUserId))
                            || (b.BlockedUserId == userId && candidateIds.Contains(b.UserId)))
                .ToList();
            var blockedByMe = blocks.Where(b => b.UserId == userId).Select(b => b.BlockedUserId).ToHashSet();
            var blockedMe = blocks.Where(b => b.BlockedUserId == userId).Select(b => b.UserId).ToHashSet();

            // 平台可加性（管理员豁免：恒有全平台权限）
            var adminIds = GetAdminUserIds(candidateIds);
            var addableIds = platform == ClientPlatform.App
                ? GetAppAddableIds(candidateIds)
                : GetWebAddableIds(candidateIds);

            return candidates
                .Where(c => !friendIds.Contains(c.Id) && !blockedByMe.Contains(c.Id) && !blockedMe.Contains(c.Id))
                .Where(c => adminIds.Contains(c.Id) || addableIds.Contains(c.Id))
                .Select(c => new FriendSearchResultDto
                {
                    UserId = c.Id,
                    Account = c.Account,
                    DisplayName = c.DisplayName,
                    Avatar = c.Avatar,
                })
                .ToList();
        }

        /// <summary>发起好友申请：校验链全部通过后落库；返回申请条目（含申请人信息，供 SignalR 推送给接收方）。</summary>
        public FriendRequestDto SendRequest(Guid fromUserId, Guid toUserId, string? message, string platform)
        {
            var msg = (message ?? string.Empty).Trim();
            if (msg.Length > MaxMessageLength)
                throw new BadRequestException($"验证留言不能超过 {MaxMessageLength} 字");

            if (toUserId == fromUserId) throw new BadRequestException("不能添加自己为好友");
            var peer = _fsql.Select<SysUserEntity>()
                .Where(u => u.Id == toUserId && !u.IsDeleted)
                .First();
            if (peer == null) throw new NotFoundException("对方用户不存在");
            if (!peer.Enabled) throw new BadRequestException("对方账号已停用，无法添加");

            if (IsFriend(fromUserId, toUserId)) throw new BadRequestException("你们已经是好友了");

            // 屏蔽优先于好友（与聊天一致：我屏蔽的/屏蔽我的都无法发起申请）
            var blocks = _fsql.Select<ChatBlockListEntity>()
                .Where(b => (b.UserId == fromUserId && b.BlockedUserId == toUserId)
                            || (b.UserId == toUserId && b.BlockedUserId == fromUserId))
                .ToList();
            if (blocks.Any(b => b.UserId == fromUserId))
                throw new BadRequestException("你已屏蔽对方，请先取消屏蔽后再添加好友");
            if (blocks.Any(b => b.BlockedUserId == fromUserId))
                throw new BadRequestException("对方暂无法接收你的好友申请");

            EnsureAddable(fromUserId, toUserId, platform);

            // 防重：双向任一待处理申请存在即不可再发（DB 过滤唯一索引兜底并发）
            var pendingExists = _fsql.Select<ChatFriendRequestEntity>()
                .Where(r => r.Status == 0
                            && ((r.FromUserId == fromUserId && r.ToUserId == toUserId)
                                || (r.FromUserId == toUserId && r.ToUserId == fromUserId)))
                .Any();
            if (pendingExists) throw new BadRequestException("你们之间已有待处理的好友申请，请等待对方处理");

            var from = _fsql.Select<SysUserEntity>().Where(u => u.Id == fromUserId).First();
            var request = new ChatFriendRequestEntity
            {
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Message = msg.Length == 0 ? null : msg,
                Status = 0,
                CreateTime = TimeHelper.Now,
            };
            request.Id = _fsql.Insert(request).ExecuteIdentity();

            _logger.LogInformation("好友申请：{FromUserId} → {ToUserId}", fromUserId, toUserId);
            return new FriendRequestDto
            {
                Id = request.Id,
                FromUserId = fromUserId,
                FromAccount = from?.Account ?? fromUserId.ToString(),
                FromDisplayName = from?.DisplayName,
                FromAvatar = from?.Avatar,
                Message = request.Message,
                Status = request.Status,
                CreateTime = request.CreateTime,
            };
        }

        /// <summary>我收到的好友申请（全部状态，最新在前）。</summary>
        public List<FriendRequestDto> GetRequests(Guid userId)
        {
            var requests = _fsql.Select<ChatFriendRequestEntity>()
                .Where(r => r.ToUserId == userId)
                .OrderByDescending(r => r.Id)
                .ToList();
            if (requests.Count == 0) return new List<FriendRequestDto>();

            var fromIds = requests.Select(r => r.FromUserId).Where(id => id != Guid.Empty).Distinct().ToList();
            var userMap = fromIds.Count == 0
                ? new Dictionary<Guid, SysUserEntity>()
                : _fsql.Select<SysUserEntity>()
                    .Where(u => fromIds.Contains(u.Id))
                    .ToList()
                    .ToDictionary(u => u.Id);

            return requests.Select(r =>
            {
                var u = userMap.GetValueOrDefault(r.FromUserId);
                return new FriendRequestDto
                {
                    Id = r.Id,
                    FromUserId = r.FromUserId,
                    FromAccount = u?.Account ?? r.FromUserId.ToString(),
                    FromDisplayName = u?.DisplayName,
                    FromAvatar = u?.Avatar,
                    Message = r.Message,
                    Status = r.Status,
                    CreateTime = r.CreateTime,
                };
            }).ToList();
        }

        /// <summary>待处理好友申请数。</summary>
        public int GetPendingCount(Guid userId)
            => (int)_fsql.Select<ChatFriendRequestEntity>()
                .Where(r => r.ToUserId == userId && r.Status == 0)
                .Count();

        /// <summary>处理好友申请：只能处理发给自己的待处理申请；同意则事务内更新状态 + 双行互加（幂等）。</summary>
        public (Guid FromUserId, FriendRequestHandledDto Result) HandleRequest(Guid userId, long requestId, bool accept)
        {
            var request = _fsql.Select<ChatFriendRequestEntity>().Where(r => r.Id == requestId).First();
            if (request == null || request.ToUserId != userId)
                throw new NotFoundException("好友申请不存在");
            if (request.Status != 0) throw new BadRequestException("该申请已处理过");

            var me = _fsql.Select<SysUserEntity>().Where(u => u.Id == userId).First();
            var myName = me == null ? string.Empty : (me.DisplayName ?? me.Account);
            var now = TimeHelper.Now;

            if (accept)
            {
                var fromUser = _fsql.Select<SysUserEntity>()
                    .Where(u => u.Id == request.FromUserId && !u.IsDeleted)
                    .First();
                if (fromUser == null) throw new BadRequestException("申请人账号已删除，无法同意");

                _fsql.Transaction(() =>
                {
                    _fsql.Update<ChatFriendRequestEntity>()
                        .Set(r => r.Status, 1)
                        .Set(r => r.HandleTime, now)
                        .Where(r => r.Id == requestId && r.Status == 0)
                        .ExecuteAffrows();

                    // 双行互加（已是好友则跳过，幂等）
                    var exists = _fsql.Select<ChatFriendshipEntity>()
                        .Where(f => (f.UserId == userId && f.FriendId == request.FromUserId)
                                    || (f.UserId == request.FromUserId && f.FriendId == userId))
                        .Any();
                    if (!exists)
                    {
                        _fsql.Insert(new List<ChatFriendshipEntity>
                        {
                            new() { UserId = userId, FriendId = request.FromUserId, CreateTime = now },
                            new() { UserId = request.FromUserId, FriendId = userId, CreateTime = now },
                        }).ExecuteAffrows();
                    }
                });
                _logger.LogInformation("好友申请已同意：申请 {RequestId}，处理人 {UserId}", requestId, userId);
            }
            else
            {
                _fsql.Update<ChatFriendRequestEntity>()
                    .Set(r => r.Status, 2)
                    .Set(r => r.HandleTime, now)
                    .Where(r => r.Id == requestId && r.Status == 0)
                    .ExecuteAffrows();
                _logger.LogInformation("好友申请已拒绝：申请 {RequestId}，处理人 {UserId}", requestId, userId);
            }

            return (request.FromUserId, new FriendRequestHandledDto
            {
                RequestId = requestId,
                Accept = accept,
                HandlerName = myName,
            });
        }

        /// <summary>删除好友：成对删两行（幂等）；会话与历史消息保留，但双方不能再互发（ChatService 好友校验拦截）。</summary>
        public void DeleteFriend(Guid userId, Guid friendId)
        {
            if (friendId == userId) throw new BadRequestException("参数无效");

            _fsql.Delete<ChatFriendshipEntity>()
                .Where(f => (f.UserId == userId && f.FriendId == friendId)
                            || (f.UserId == friendId && f.FriendId == userId))
                .ExecuteAffrows();
            _logger.LogInformation("删除好友：{UserId} 与 {FriendId}", userId, friendId);
        }

        /// <summary>设置/清除好友备注：仅更新我方持有行（双行模式互不影响，对方看到的仍是原昵称）；空串=清除备注。</summary>
        public void SetRemark(Guid userId, Guid friendUserId, string? remark)
        {
            if (friendUserId == userId) throw new BadRequestException("参数无效");
            var name = (remark ?? string.Empty).Trim();
            if (name.Length > MaxRemarkLength)
                throw new BadRequestException($"备注不能超过 {MaxRemarkLength} 个字");

            var exists = _fsql.Select<ChatFriendshipEntity>()
                .Where(f => f.UserId == userId && f.FriendId == friendUserId)
                .Any();
            if (!exists) throw new BadRequestException("对方不是你的好友，无法设置备注");

            _fsql.Update<ChatFriendshipEntity>()
                .Set(f => f.Remark, name.Length == 0 ? null : name)
                .Where(f => f.UserId == userId && f.FriendId == friendUserId)
                .ExecuteAffrows();
            _logger.LogInformation("好友备注：{UserId} → {FriendUserId} = {Remark}", userId, friendUserId, name);
        }

        // ===== 校验辅助 =====

        /// <summary>是否好友（双行模式两行成对存在，查我方持有行即可）。</summary>
        private bool IsFriend(Guid userId, Guid peerId)
            => _fsql.Select<ChatFriendshipEntity>()
                .Where(f => f.UserId == userId && f.FriendId == peerId)
                .Any();

        /// <summary>单个用户的平台可加性校验（与 SearchUsers 批量过滤同一口径）：管理员恒可加。</summary>
        private void EnsureAddable(Guid fromUserId, Guid toUserId, string platform)
        {
            if (IsAdminUser(toUserId)) return;

            if (platform == ClientPlatform.App)
            {
                var codes = _fsql.Select<SysUserAppPermEntity>()
                    .Where(p => p.UserId == toUserId)
                    .ToList(p => p.PermCode);
                if (!codes.Contains(AppPerm.Friends) || !codes.Contains(AppPerm.Login))
                    throw new BadRequestException("对方未开通手机端好友功能，无法添加");
            }
            else
            {
                var hasRole = HasAnyEnabledRole(toUserId);
                if (!hasRole)
                    throw new BadRequestException("对方未开通 PC 端访问权限，无法添加");
            }
        }

        /// <summary>用户是否有任意启用角色（PC 端可被搜到的口径，与登录准入一致）。</summary>
        private bool HasAnyEnabledRole(Guid userId)
        {
            var roleIds = _fsql.Select<SysRoleEntity>().Where(r => r.Enabled).ToList(r => r.Id);
            return roleIds.Count > 0
                && _fsql.Select<SysUserRoleEntity>()
                    .Where(ur => ur.UserId == userId && roleIds.Contains(ur.RoleId))
                    .Any();
        }

        /// <summary>候选集中的管理员 Id 集合（任一 IsAdmin 角色的用户 + 内置 admin 账号，与 AppPermService 口径一致）。</summary>
        private HashSet<Guid> GetAdminUserIds(List<Guid> candidateIds)
        {
            var adminRoleIds = _fsql.Select<SysRoleEntity>()
                .Where(r => r.IsAdmin || r.Code == JwtHelper.AdminRole)
                .ToList(r => r.Id);

            var ids = adminRoleIds.Count == 0
                ? new HashSet<Guid>()
                : _fsql.Select<SysUserRoleEntity>()
                    .Where(ur => adminRoleIds.Contains(ur.RoleId) && candidateIds.Contains(ur.UserId))
                    .ToList(ur => ur.UserId)
                    .ToHashSet();

            var builtIn = _fsql.Select<SysUserEntity>()
                .Where(u => candidateIds.Contains(u.Id) && u.Account == BuiltInAdminAccount)
                .ToList(u => u.Id);
            foreach (var id in builtIn) ids.Add(id);
            return ids;
        }

        /// <summary>单个用户是否管理员。</summary>
        private bool IsAdminUser(Guid userId)
        {
            var account = _fsql.Select<SysUserEntity>().Where(u => u.Id == userId).First(u => u.Account);
            if (account == null) return false;
            if (string.Equals(account, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase)) return true;

            var adminRoleIds = _fsql.Select<SysRoleEntity>()
                .Where(r => r.IsAdmin || r.Code == JwtHelper.AdminRole)
                .ToList(r => r.Id);
            return adminRoleIds.Count > 0
                && _fsql.Select<SysUserRoleEntity>()
                    .Where(ur => ur.UserId == userId && adminRoleIds.Contains(ur.RoleId))
                    .Any();
        }

        /// <summary>候选集中手机端可加的用户（同时开通 app-friends 与 app-login；管理员已在调用方豁免）。</summary>
        private HashSet<Guid> GetAppAddableIds(List<Guid> candidateIds)
        {
            var required = new[] { AppPerm.Friends, AppPerm.Login };
            var rows = _fsql.Select<SysUserAppPermEntity>()
                .Where(p => candidateIds.Contains(p.UserId) && required.Contains(p.PermCode))
                .ToList(p => new { p.UserId, p.PermCode });

            return rows.GroupBy(p => p.UserId)
                .Where(g => required.All(code => g.Any(p => p.PermCode == code)))
                .Select(g => g.Key)
                .ToHashSet();
        }

        /// <summary>候选集中 PC 端可加的用户（有任意启用角色；管理员已在调用方豁免）。</summary>
        private HashSet<Guid> GetWebAddableIds(List<Guid> candidateIds)
        {
            var roleIds = _fsql.Select<SysRoleEntity>().Where(r => r.Enabled).ToList(r => r.Id);
            if (roleIds.Count == 0) return new HashSet<Guid>();

            return _fsql.Select<SysUserRoleEntity>()
                .Where(ur => roleIds.Contains(ur.RoleId) && candidateIds.Contains(ur.UserId))
                .ToList(ur => ur.UserId)
                .ToHashSet();
        }
    }
}
