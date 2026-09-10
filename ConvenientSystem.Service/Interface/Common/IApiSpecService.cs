using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// API 文档生成器：扫描 C# 项目的 Controller 源码，解析出接口清单与 DTO 类型树（IR），
    /// 再由各 IApiExporter 转换为 OpenAPI / Postman Collection / Markdown 等格式的 API 数据文件，
    /// 供 Apifox / Postman 等工具导入。无状态纯解析，不落库。
    /// </summary>
    public interface IApiSpecService
    {
        /// <summary>支持的导出格式列表（DI 注册的全部 Exporter）。</summary>
        List<ApiSpecFormatDto> GetFormats();

        /// <summary>
        /// 扫描目录下的 Controller 文件（文件名 *Controller.cs，排除 bin/obj/node_modules 等）。
        /// </summary>
        /// <param name="rootDir">项目根目录（绝对路径）。</param>
        List<ApiSpecFileDto> ScanControllers(string rootDir);

        /// <summary>
        /// 扫描解决方案（.sln/.slnx）或目录内全部接口，返回接口级清单（前端勾选后生成文档）。
        /// 纯语法解析（路由/方法/注释），不建类型索引不展开 DTO，与文件级扫描同等成本。
        /// </summary>
        /// <param name="solutionPath">解决方案文件路径，或直接传目录路径。</param>
        List<ApiSpecSolutionEndpointDto> ScanSolution(string solutionPath);

        /// <summary>
        /// 解析选中的 Controller 文件集合 → IR 文档（接口清单 + DTO 类型树），前端预览用。
        /// </summary>
        /// <param name="rootDir">项目根目录（用于解析相对路径与扫描同项目 DTO 定义）。</param>
        /// <param name="files">选中的 Controller 相对路径集合（逗号分隔）。</param>
        /// <param name="title">文档标题（空则默认）。</param>
        /// <param name="baseUrl">服务器地址（空则默认 http://localhost）。</param>
        /// <param name="solutionPath">原始解决方案文件或目录路径，用于保持扫描与解析范围一致。</param>
        ApiSpecDocumentDto Parse(string rootDir, string files, string? title, string? baseUrl, string? solutionPath = null);

        /// <summary>生成指定格式的 API 数据文件内容（预览与下载共用）。</summary>
        /// <param name="format">格式标识（见 GetFormats）。</param>
        /// <param name="only">旧版接口筛选键，保留以兼容已有调用。</param>
        /// <param name="solutionPath">原始解决方案文件或目录路径，用于保持扫描与生成范围一致。</param>
        /// <param name="selectionKeys">扫描期返回的稳定接口选择标识集合。</param>
        ApiSpecExportDto Export(string rootDir, string files, string format, string? title, string? baseUrl,
            string? only = null, string? solutionPath = null, IEnumerable<string>? selectionKeys = null);
    }
}
