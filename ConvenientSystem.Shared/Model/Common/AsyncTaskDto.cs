using System.Text.Json.Serialization;

namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 异步任务进度（统一任务中心）：全部后台耗时操作（解决方案扫描/解析生成/Apifox 导入/批量删除等）
    /// 的公共进度载体。启动接口返回初始快照，前端提交后经顶栏异步任务面板统一展示实时进度；
    /// 云端宿主另有 SignalR 定向推送（事件 AsyncTaskProgress），桌面本地宿主靠轮询。
    /// </summary>
    public class AsyncTaskDto
    {
        /// <summary>任务 ID：轮询进度 / 复用结果的标识。</summary>
        public Guid TaskId { get; set; }

        /// <summary>任务类型：apispec-scan / apispec-generate / apifox-import / apifox-delete（并发拒绝按类型判定）。</summary>
        public string Kind { get; set; } = "";

        /// <summary>展示标题（如“解决方案扫描”“Apifox 批量删除”）。</summary>
        public string Title { get; set; } = "";

        /// <summary>任务状态：running / succeeded / failed。</summary>
        public string Status { get; set; } = "";

        /// <summary>业务阶段（如 indexing/scanning/exporting/listing/deleting），完成或失败后为空。</summary>
        public string Phase { get; set; } = "";

        /// <summary>当前步骤描述（如“正在导入分组：YunHan.Core/OrderController”）。</summary>
        public string Current { get; set; } = "";

        /// <summary>总数（文件数/批次数/接口数）；阶段开始前为 0（前端显示不确定进度）。</summary>
        public int Total { get; set; }

        /// <summary>已完成数。</summary>
        public int Completed { get; set; }

        /// <summary>失败数（部分失败不中断任务，如删除单个接口失败）。</summary>
        public int Failed { get; set; }

        /// <summary>任务失败原因（status=failed 时）。</summary>
        public string? Error { get; set; }

        /// <summary>终态摘要（如“已生成 1234 个接口的文档”）：轮询/推送不携带 Result（大负载），
        /// 完成通知与任务卡片展示用；页面需要完整结果时经 GetResult 端点按需拉取。</summary>
        public string Summary { get; set; } = "";

        /// <summary>任务开始时间（UTC）。</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>任务结束时间（UTC；运行中为 null）。完成后任务保留 30 分钟。</summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>任务结果：结构随业务类型（扫描接口清单 / 文档+导出内容 / Apifox 导入聚合结果 / 删除统计），运行中为 null。</summary>
        public object? Result { get; set; }

        /// <summary>业务私有负载：不序列化给前端（如生成任务的接口选择标识，ReExport 换格式导出时复用）。</summary>
        [JsonIgnore]
        public object? Payload { get; set; }
    }
}
