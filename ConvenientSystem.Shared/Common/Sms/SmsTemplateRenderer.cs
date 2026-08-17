using System.Text.RegularExpressions;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信模板变量替换工具
    /// 支持 {姓名}、{公司} 等变量，替换为实际值
    /// </summary>
    public static class SmsTemplateRenderer
    {
        /// <summary>
        /// 渲染模板内容（将 {变量} 替换为实际值）
        /// </summary>
        /// <param name="template">模板内容（含 {变量}）</param>
        /// <param name="variables">变量字典（key 为变量名，不含大括号）</param>
        /// <returns>替换后的内容</returns>
        public static string Render(string template, Dictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return Regex.Replace(template, @"\{([^{}]+)\}", match =>
            {
                var key = match.Groups[1].Value.Trim();
                return variables.TryGetValue(key, out var value) ? value : match.Value;
            });
        }

        /// <summary>
        /// 提取模板中所有变量名
        /// </summary>
        public static List<string> ExtractVariables(string template)
        {
            if (string.IsNullOrEmpty(template)) return new List<string>();
            return Regex.Matches(template, @"\{([^{}]+)\}")
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToList();
        }
    }
}
