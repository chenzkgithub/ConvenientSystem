namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 代码命名翻译结果：中文 → 英文单词列表
    /// </summary>
    public class CodeNamingTranslateDto
    {
        /// <summary>原始中文输入</summary>
        public string Original { get; set; } = string.Empty;

        /// <summary>翻译后的英文短语（空格分隔）</summary>
        public string Translated { get; set; } = string.Empty;

        /// <summary>拆分后的英文单词数组</summary>
        public List<string> Words { get; set; } = new();
    }
}
