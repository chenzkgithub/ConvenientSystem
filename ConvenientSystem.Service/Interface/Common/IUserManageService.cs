using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 用户管理业务服务：用户增删改查、启停、重置密码、分配角色。密码以 PBKDF2 哈希存储。
    /// </summary>
    public interface IUserManageService
    {
        /// <summary>用户列表（含所属角色，不含头像——头像经 GetUser 单查）。</summary>
        List<UserManageDto> GetUsers();

        /// <summary>单用户详情（含头像与角色，供编辑弹窗使用）。</summary>
        UserManageDto GetUser(Guid id);

        /// <summary>新增或更新用户（新增必须带密码；编辑时密码留空表示不修改），同时保存角色分配。</summary>
        void SaveUser(UserSaveDto dto);

        /// <summary>启用/停用用户。</summary>
        void SetEnabled(SetEnabledDto dto);

        /// <summary>重置密码。</summary>
        void ResetPassword(ResetPasswordDto dto);

        /// <summary>删除用户（连同角色关联）。</summary>
        void Delete(Guid id);

        /// <summary>恢复已删除用户（软删除还原，恢复后默认启用）。</summary>
        void Restore(Guid id);
    }
}
