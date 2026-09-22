using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// API 文档生成器：扫描 C# 项目的 Controller 源码，解析出接口清单与 DTO 类型树（IR），
    /// 再由各 IApiExporter 转换为 OpenAPI / Postman Collection / Markdown 等格式的 API 数据文件，
    /// 供 Apifox / Postman 等工具导入。无状态纯解析，不落库。
    /// 扫描与解析生成为后台任务（统一 AsyncTaskCenter 进度表，见 AsyncTaskDto）：
    /// 一次解析同时产出 IR 文档与导出内容；任务进内存任务中心保留 30 分钟供 ReExport 复用。
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

        /// <summary>生成指定格式的 API 数据文件内容（旧下载端点用；内部重新解析源码）。</summary>
        /// <param name="format">格式标识（见 GetFormats）。</param>
        /// <param name="only">旧版接口筛选键，保留以兼容已有调用。</param>
        /// <param name="solutionPath">原始解决方案文件或目录路径，用于保持扫描与生成范围一致。</param>
        /// <param name="selectionKeys">扫描期返回的稳定接口选择标识集合。</param>
        ApiSpecExportDto Export(string rootDir, string files, string format, string? title, string? baseUrl,
            string? only = null, string? solutionPath = null, IEnumerable<string>? selectionKeys = null);

        /// <summary>启动解决方案扫描后台任务：同步校验路径后返回任务初始进度快照，扫描（含类型索引）后台执行。</summary>
        /// <param name="request">解决方案文件（.sln/.slnx）或目录路径。</param>
        /// <param name="userId">发起人（云端登录用户；桌面端传 null），任务进度仅本人可查。</param>
        AsyncTaskDto StartScan(ApiSpecScanTaskRequest request, Guid? userId);

        /// <summary>
        /// 启动解析并生成后台任务：一次解析同时产出 IR 文档与指定格式导出内容，
        /// 替代原先 Parse + Preview 两次请求两次全量解析的组合。
        /// </summary>
        /// <param name="userId">发起人（云端登录用户；桌面端传 null），任务进度仅本人可查。</param>
        AsyncTaskDto StartGenerate(ApiSpecGenerateRequest request, Guid? userId);

        /// <summary>复用已完成生成任务的解析结果按新格式/标题重新导出（不重新解析源码）。</summary>
        ApiSpecExportDto ReExport(ApiSpecReExportRequest request, Guid? userId);
    }
}
