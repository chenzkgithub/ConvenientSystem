namespace ConvenientSystem.Shared.Model.Sms
{
    /// <summary>
    /// 短信模板 DTO
    /// </summary>
    public class SmsTemplateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Signature { get; set; } = "zk";
        public string Category { get; set; } = "通知";
        public bool Enabled { get; set; }
        /// <summary>创建人账号（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByAccount { get; set; }
        /// <summary>创建人姓名（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByName { get; set; }
        public DateTime? CreateTime { get; set; }
    }

    /// <summary>
    /// 短信任务 DTO
    /// </summary>
    public class SmsTaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public string? TemplateName { get; set; }
        public DateTime SendTime { get; set; }
        public byte Status { get; set; }
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        /// <summary>创建人账号（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByAccount { get; set; }
        /// <summary>创建人姓名（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByName { get; set; }
        public DateTime? CreateTime { get; set; }
    }

    /// <summary>
    /// 创建任务请求
    /// </summary>
    public class CreateSmsTaskRequest
    {
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public DateTime SendTime { get; set; }
        public List<SmsRecipientDto> Recipients { get; set; } = new();
    }

    /// <summary>
    /// 收件人 DTO
    /// </summary>
    public class SmsRecipientDto
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte Status { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? SentTime { get; set; }
    }

    /// <summary>
    /// 发送日志 DTO
    /// </summary>
    public class SmsLogDto
    {
        public long Id { get; set; }
        public int TaskId { get; set; }
        public string? TaskName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ProviderMsgId { get; set; }
        public byte Status { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
        public DateTime CreateTime { get; set; }
    }

    /// <summary>
    /// 服务商配置 DTO（前端交互用，密钥字段脱敏）
    /// </summary>
    public class SmsProviderConfigDto
    {
        public int Id { get; set; }

        /// <summary>配置名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>服务商类型：aliyun / ihuyi</summary>
        public string ProviderType { get; set; } = "aliyun";
        public string AccessKeyId { get; set; } = string.Empty;
        public string AccessKeySecret { get; set; } = string.Empty;
        public string DefaultSignature { get; set; } = "zk";
        /// <summary>模板 Code（阿里云需要）</summary>
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>关联本地短信模板 Id（SmsTemplate.Id）</summary>
        public int? TemplateId { get; set; }

        /// <summary>关联模板名称（列表展示用）</summary>
        public string? TemplateName { get; set; }

        /// <summary>是否为默认配置</summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        public DateTime? CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }
    }

    /// <summary>
    /// 配额 DTO
    /// </summary>
    public class SmsQuotaDto
    {
        public int DailyMax { get; set; } = 100;
        public int MonthlyMax { get; set; } = 3000;
        public int DailyUsed { get; set; }
        public int MonthlyUsed { get; set; }
    }

    /// <summary>
    /// 发送统计 DTO
    /// </summary>
    public class SmsStatisticsDto
    {
        public int TodayCount { get; set; }
        public int MonthCount { get; set; }
        public double SuccessRate { get; set; }
        public int DailyRemaining { get; set; }
    }

    /// <summary>
    /// 测试发送请求
    /// </summary>
    public class SmsTestSendRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Signature { get; set; } = "zk";
    }

    /// <summary>
    /// 测试发送结果
    /// </summary>
    public class SmsTestSendResultDto
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ProviderMsgId { get; set; }
        public int CostMs { get; set; }
        /// <summary>实际使用的服务商标识</summary>
        public string Provider { get; set; } = "unknown";
    }

    /// <summary>
    /// 模板预览请求
    /// </summary>
    public class PreviewTemplateRequest
    {
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string>? Variables { get; set; }
    }

    /// <summary>
    /// 模板渲染预览结果
    /// </summary>
    public class TemplatePreviewDto
    {
        public string Rendered { get; set; } = string.Empty;
    }

    /// <summary>
    /// 启用状态切换结果
    /// </summary>
    public class ToggleEnabledDto
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 任务详情（任务本体 + 收件人列表）
    /// </summary>
    public class SmsTaskDetailDto
    {
        public SmsTaskDto Task { get; set; } = new();
        public List<SmsRecipientDto> Recipients { get; set; } = new();
    }

    /// <summary>
    /// 任务创建结果
    /// </summary>
    public class SmsTaskCreatedDto
    {
        public int Id { get; set; }
        /// <summary>Hangfire 作业 Id</summary>
        public string? HangfireJobId { get; set; }
    }
}
