using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.ApiSpec
{
    /// <summary>
    /// API 数据文件导出器契约：IR（ApiSpecDocumentDto）进、目标格式文本出。
    /// 加新格式 = 新建一个实现类并在 ServicesExtent 注册 Singleton，格式列表自动出现。
    /// </summary>
    public interface IApiExporter
    {
        /// <summary>格式标识（如 openapi3-json，URL 参数用）。</summary>
        string Format { get; }
        /// <summary>展示名（如 OpenAPI 3.0 (JSON)）。</summary>
        string DisplayName { get; }
        /// <summary>文件扩展名（含点）。</summary>
        string FileExtension { get; }
        /// <summary>下载 Content-Type。</summary>
        string ContentType { get; }
        /// <summary>格式说明（适合导入哪些工具）。</summary>
        string Description { get; }
        /// <summary>建议文件名前缀（完整名 = 前缀 + 扩展名）。</summary>
        string FileNameBase { get; }

        /// <summary>把 IR 文档转换为该格式文本。</summary>
        string Export(ApiSpecDocumentDto doc);
    }
}
