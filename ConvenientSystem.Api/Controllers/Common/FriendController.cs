using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 好友用户端接口：搜索（按发起方平台过滤可加用户）/ 申请 / 处理 / 删除；好友列表复用 Chat/Contacts（通讯录=好友）。
    /// PC 端不挂菜单权限码；手机端（platform=app）要求 app-friends 权限点（好友功能与聊天 app-chat 独立）。
    /// 平台过滤口径（与权限体系一致）：手机端发起要求对方开通 app-friends+app-login；
    /// PC 端发起要求对方有任意启用角色；管理员恒可被添加。目标用户恒取自 JWT，不接受请求体传入，防越权。
    /// 实时推送：新申请推 FriendRequest 给接收方；处理结果推 FriendRequestHandled 给申请人。
    /// </summary>
    [Area("Common")]
    [Authorize]
    [PermissionAuthorize(AppPerm.Friends, Platform = ClientPlatform.App)]
    public class FriendController : BaseController
    {
        private readonly IFriendService _service;
        private readonly IHubContext<ChatHub> _hubContext;

        public FriendController(IFriendService service, IHubContext<ChatHub> hubContext)
        {
            _service = service;
            _hubContext = hubContext;
        }

        /// <summary>当前请求平台（JWT platform claim，缺省 web）：搜索/申请的平台过滤依据。</summary>
        private string CurrentPlatform
            => User.FindFirst(JwtHelper.PlatformClaim)?.Value ?? ClientPlatform.Web;

        /// <summary>搜索可添加的用户：按发起方平台过滤（对方在发起方所在平台可被添加），排除自己/已有好友/双向屏蔽。</summary>
        [HttpGet]
        public ActionResult<List<FriendSearchResultDto>> Search([FromQuery] string keyword)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.SearchUsers(userId, keyword, CurrentPlatform));
        }

        /// <summary>我收到的好友申请（全部状态，最新在前）。</summary>
        [HttpGet]
        public ActionResult<List<FriendRequestDto>> Requests()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(_service.GetRequests(userId));
        }

        /// <summary>待处理好友申请数（入口角标轮询）。</summary>
        [HttpGet]
        public ActionResult<FriendRequestCountDto> PendingCount()
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            return Ok(new FriendRequestCountDto { Count = _service.GetPendingCount(userId) });
        }

        /// <summary>发起好友申请：校验非自己/对方启用/非好友/无屏蔽/无待处理申请/平台可加；实时推送给对方。</summary>
        [HttpPost]
        public async Task<IActionResult> Send([FromBody] FriendSendRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var dto = _service.SendRequest(userId, request.ToUserId, request.Message, CurrentPlatform);
            await _hubContext.Clients.User(request.ToUserId.ToString())
                .SendAsync("FriendRequest", dto);
            return Ok(dto);
        }

        /// <summary>处理好友申请（同意则双方互加好友）；处理结果实时推送给申请人。</summary>
        [HttpPost]
        public async Task<IActionResult> Handle([FromBody] FriendHandleRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            var (fromUserId, result) = _service.HandleRequest(userId, request.RequestId, request.Accept);
            await _hubContext.Clients.User(fromUserId.ToString())
                .SendAsync("FriendRequestHandled", result);
            return Ok(result);
        }

        /// <summary>删除好友（成对删两行，幂等；会话与历史消息保留，双方不能再互发消息）。</summary>
        [HttpPost]
        public IActionResult Delete([FromQuery] Guid friendId)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.DeleteFriend(userId, friendId);
            return Ok();
        }

        /// <summary>设置/清除好友备注：仅影响我方视角的展示名（消息列表/通讯录/聊天标题优先显示备注）；空串=清除。</summary>
        [HttpPost]
        public IActionResult SetRemark([FromBody] FriendRemarkSaveRequest request)
        {
            if (!TryGetDbUserId(out var userId, out var error)) return error!;
            _service.SetRemark(userId, request.FriendUserId, request.Remark);
            return Ok(new { ok = true });
        }

        /// <summary>当前用户身份兜底（未登录 401；兜底账号 userId=Guid.Empty 允许通过，Service 返回空结果）。</summary>
        private bool TryGetDbUserId(out Guid userId, out ActionResult? error)
        {
            var id = CurrentUserId;
            if (!id.HasValue)
            {
                userId = Guid.Empty;
                error = Unauthorized();
                return false;
            }
            userId = id.Value;
            error = null;
            return true;
        }
    }
}
