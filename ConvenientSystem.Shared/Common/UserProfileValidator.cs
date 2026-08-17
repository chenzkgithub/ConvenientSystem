using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Sms;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// SysUser 个人资料字段的统一清洗与校验：
    /// 个人资料接口（本人修改）与用户管理接口（管理员维护）共用同一套规则，避免两处标准不一致。
    /// 约定：空白一律归一为 null 存库，便于前端判空。
    /// </summary>
    public static class UserProfileValidator
    {
        /// <summary>头像 base64 字符串长度上限（约 1MB 文本 ≈ 750KB 图片），超出视为前端未压缩。</summary>
        public const int AvatarMaxLength = 1_000_000;

        /// <summary>显示名称：必填，最长 50。</summary>
        public static string NormalizeDisplayName(string? value)
        {
            var name = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) throw new BadRequestException("显示名称不能为空");
            if (name.Length > 50) throw new BadRequestException("显示名称不能超过 50 个字符");
            return name;
        }

        /// <summary>手机号：可空；非空须为合法的中国大陆手机号。</summary>
        public static string? NormalizePhone(string? value)
        {
            var phone = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(phone)) return null;
            if (!SmsPhoneHelper.IsValid(phone)) throw new BadRequestException("手机号格式不正确");
            return phone;
        }

        /// <summary>邮箱：可空；非空做基本格式与长度校验。</summary>
        public static string? NormalizeEmail(string? value)
        {
            var email = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(email)) return null;
            if (email.Length > 100) throw new BadRequestException("邮箱不能超过 100 个字符");
            if (!Regex.IsMatch(email, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"))
                throw new BadRequestException("邮箱格式不正确");
            return email;
        }

        /// <summary>备注：可空，最长 200。</summary>
        public static string? NormalizeRemark(string? value)
        {
            var remark = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(remark)) return null;
            if (remark.Length > 200) throw new BadRequestException("备注不能超过 200 个字符");
            return remark;
        }

        /// <summary>
        /// 头像：可空；非空须为 data:image/*;base64 内联图片并受长度上限约束。
        /// 只接受内联 base64——头像随用户记录一起存库，不引入静态文件目录，
        /// 桌面壳与接口服务是两个独立发布产物，共用文件目录会带来同步问题。
        /// </summary>
        public static string? NormalizeAvatar(string? value)
        {
            var avatar = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(avatar)) return null;
            if (!avatar.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("头像格式不正确，请重新选择图片");
            if (avatar.Length > AvatarMaxLength)
                throw new BadRequestException("头像图片过大，请选择更小的图片");
            return avatar;
        }
    }
}
