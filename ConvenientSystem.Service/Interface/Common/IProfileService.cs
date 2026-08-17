using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 个人资料业务服务：当前登录用户查看/修改自己的资料与密码。
    /// 与用户管理（IUserManageService）区分：本服务只能操作调用者自己，无需"用户管理"权限。
    /// </summary>
    public interface IProfileService
    {
        /// <summary>读取指定用户的个人资料。</summary>
        Task<ProfileDto> GetProfileAsync(Guid userId);

        /// <summary>修改显示名称（账号不可改）。</summary>
        Task SaveProfileAsync(Guid userId, ProfileSaveDto dto);

        /// <summary>修改本人密码：校验原密码后以哈希存储新密码。</summary>
        Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto);
    }
}
