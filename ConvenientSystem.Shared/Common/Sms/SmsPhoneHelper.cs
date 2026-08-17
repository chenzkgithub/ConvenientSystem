using System.Text.RegularExpressions;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 手机号处理助手：格式校验与展示脱敏（原先在多个控制器中各自重复实现，统一到此处）。
    /// </summary>
    public static class SmsPhoneHelper
    {
        /// <summary>校验是否为合法的中国大陆手机号</summary>
        public static bool IsValid(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return false;
            return Regex.IsMatch(phone.Trim(), @"^1[3-9]\d{9}$");
        }

        /// <summary>脱敏手机号用于展示（保留前 3 位与后 4 位）</summary>
        public static string Mask(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 7) return phone;
            return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 4);
        }
    }
}
