using System.Security.Claims;
using ConvenientSystem.Shared.Common.Security;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Auth
{
    /// <summary>
    /// SignalR 用户标识提供者：从 JWT claim 取数据库用户 Guid 作为连接的用户标识，
    /// 供服务端 Clients.User(guid).SendAsync(...) 定向推送。
    /// 与 BaseController.CurrentUserId 读取同一 claim（JwtHelper.UserIdClaim，兜底 ClaimTypes.NameIdentifier）。
    /// </summary>
    public class SignalRUserIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection)
        {
            var raw = connection.User?.FindFirst(JwtHelper.UserIdClaim)?.Value
                      ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id.ToString() : null;
        }
    }
}
