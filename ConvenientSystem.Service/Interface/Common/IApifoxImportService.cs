using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Apifox 同步服务：配置归属当前用户，服务端保管 Access Token 并代为调用 Apifox 开放 API。
    /// 导入与批量删除均为后台任务（统一 AsyncTaskCenter 进度表），启动后返回任务初始进度快照。
    /// </summary>
    public interface IApifoxImportService
    {
        /// <summary>读取当前用户的 Access Token 保存状态，不返回令牌内容。</summary>
        ApifoxAccessTokenStatusDto GetMyAccessTokenStatus();

        /// <summary>保存或清除当前用户的 Access Token。</summary>
        void SaveMyAccessToken(ApifoxAccessTokenSaveRequest request);

        /// <summary>查看当前用户的 Access Token 明文：验证登录密码后解密返回；未配置或密码错误返回 null。</summary>
        string? RevealMyAccessToken(string password);

        /// <summary>
        /// 启动 OpenAPI 分批导入任务：按 tag（命名空间/Controller）把文档拆成多个子文档逐批导入，
        /// 每批完成即更新进度。同一用户已有进行中的任务时抛出业务异常。
        /// </summary>
        AsyncTaskDto StartImport(ApifoxImportRequest request, Guid userId);

        /// <summary>启动批量删除任务：删除项目全部接口，或指定目录（含子目录）下的接口；删除后顺带清理空目录。</summary>
        AsyncTaskDto StartDelete(ApifoxDeleteRequest request, Guid userId);

        /// <summary>强制终止当前用户指定类型（apifox-import/apifox-delete）的进行中任务，解锁被超时任务卡住的同类操作；
        /// 返回是否终止了任务。</summary>
        bool CancelRunning(string? kind, Guid userId);

        /// <summary>查询任务进度；任务不存在、已过期（完成超 30 分钟）或非本人任务时返回 null。</summary>
        AsyncTaskDto? GetTaskProgress(Guid taskId, Guid userId);

        /// <summary>读取已完成任务的完整 Result（轮询快照不带 Result，终态后页面按需拉取）；
        /// 任务不存在、未完成或已过期时返回 null。</summary>
        AsyncTaskDto? GetTaskResult(Guid taskId, Guid userId);
    }
}
