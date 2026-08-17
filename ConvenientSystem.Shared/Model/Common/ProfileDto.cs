namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 当前登录用户的个人资料（账号只读，不可自行修改）。
    /// </summary>
    public class ProfileDto
    {
        public string Account { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        /// <summary>头像：data:image/...;base64 内联图片</summary>
        public string? Avatar { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>修改个人资料请求：账号与启用状态不可自行修改。</summary>
    public class ProfileSaveDto
    {
        public string? DisplayName { get; set; }
        /// <summary>头像：data:image/...;base64；传空字符串表示清除头像</summary>
        public string? Avatar { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>修改本人密码请求：须校验原密码。</summary>
    public class ChangePasswordDto
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
