namespace ConvenientSystem.Shared.Model.Email
{
    /// <summary>
    /// 邮件 SMTP 配置 DTO
    /// </summary>
    public class EmailConfigDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SmtpServer { get; set; } = "smtp.qq.com";
        public int SmtpPort { get; set; } = 587;
        public string Account { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromName { get; set; } = "系统通知";
        public bool EnableSsl { get; set; } = true;
        public bool IsDefault { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime? CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }
    }

    /// <summary>
    /// 邮件任务 DTO
    /// </summary>
    public class EmailTaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Recipients { get; set; } = string.Empty;
        public string ScheduleType { get; set; } = "once";
        public DateTime? SendTime { get; set; }
        public string? CronExpression { get; set; }
        public string? WeekDays { get; set; }
        public string? DailyTime { get; set; }
        public bool Enabled { get; set; }
        public byte Status { get; set; }
        public DateTime? LastSendTime { get; set; }
        /// <summary>创建人账号（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByAccount { get; set; }
        /// <summary>创建人姓名（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByName { get; set; }
        public DateTime? CreateTime { get; set; }
    }

    /// <summary>
    /// 邮件发送日志 DTO
    /// </summary>
    public class EmailLogDto
    {
        public long Id { get; set; }
        public int TaskId { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public string Recipients { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public byte Status { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
        /// <summary>创建人账号（关联 SysUser 查询；系统自动发送时为“系统”）</summary>
        public string? CreatedByAccount { get; set; }
        /// <summary>创建人姓名（关联 SysUser 查询，仅展示用）</summary>
        public string? CreatedByName { get; set; }
        public DateTime CreateTime { get; set; }
    }

    /// <summary>
    /// 测试发送请求
    /// </summary>
    public class EmailTestSendRequest
    {
        public string Recipients { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// 测试发送结果
    /// </summary>
    public class EmailTestSendResultDto
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
    }

    /// <summary>
    /// 任务创建结果
    /// </summary>
    public class EmailTaskCreatedDto
    {
        public int Id { get; set; }
        /// <summary>Hangfire 作业 Id</summary>
        public string? HangfireJobId { get; set; }
    }

    /// <summary>
    /// 启用状态切换结果
    /// </summary>
    public class EmailTaskToggleDto
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 立即执行结果
    /// </summary>
    public class EmailTaskRunNowDto
    {
        /// <summary>Hangfire 作业 Id</summary>
        public string? HangfireJobId { get; set; }
    }
}
