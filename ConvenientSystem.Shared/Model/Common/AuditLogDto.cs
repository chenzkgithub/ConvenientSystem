namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 审计日志展示 DTO（对外返回）。
    /// </summary>
    public class AuditLogDto
    {
        public long Id { get; set; }
        public Guid? UserId { get; set; }
        public string Account { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string? ParamSummary { get; set; }
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public int CostMs { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
