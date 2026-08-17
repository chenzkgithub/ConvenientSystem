using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 代码命名翻译服务：中文 → 英文单词列表
    /// </summary>
    public interface ICodeNamingService
    {
        /// <summary>将中文文本翻译为英文，返回拆分后的单词数组</summary>
        CodeNamingTranslateDto Translate(string text);
    }
}
