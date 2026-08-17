namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 错误日志展示 DTO（对外返回）。
    /// </summary>
    public class ErrorLogDto
    {
        public long Id { get; set; }
        public string Account { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string ExceptionType { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string Ip { get; set; } = string.Empty;
        public DateTime CreateTime { get; set; }
    }
}
