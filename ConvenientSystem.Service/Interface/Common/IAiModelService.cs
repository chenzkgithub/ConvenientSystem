using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// AI 模型管理：系统配置页 AI 页签的模型 CRUD / 连通测试 / 默认切换。
    /// CRUD 由 sys-config:save 权限保护（控制器层）；ApiKey 加密存储、永不回传明文。
    /// </summary>
    public interface IAiModelService
    {
        /// <summary>管理列表（含 HasKey，不含明文 Key）</summary>
        List<AiModelListItemDto> List();

        /// <summary>新增/编辑（Id=0 新增；ApiKey 留空保持原值）；保存后归一化唯一默认</summary>
        AiModelListItemDto Save(AiModelSaveRequest request);

        /// <summary>连通性测试（Id>0 且 ApiKey 为空时用库中已存 Key）</summary>
        AiModelTestResult Test(AiModelTestRequest request);

        /// <summary>设为全局默认（须为启用状态）</summary>
        void SetDefault(int id);

        /// <summary>删除模型（删除默认后自动归一化默认）</summary>
        void Delete(int id);

        /// <summary>
        /// 解析可用模型：全局默认 → 首个启用 → 都没有则抛异常（全员统一使用默认模型，用户不可选）。
        /// 返回含解密 Key 的连接配置（仅内存传递）；在请求线程调用后传值给后台任务。
        /// </summary>
        AiModelConfig ResolveUsableModel();
    }
}
