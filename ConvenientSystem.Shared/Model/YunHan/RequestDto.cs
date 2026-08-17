namespace ConvenientSystem.Shared.Model.YunHan
{
    public class RequestDto
    {
        public string? fullCode { get; set; }
        public string? userName { get; set; }
        public string? month { get; set; }
        public string? orderby { get; set; }
        /// <summary>钉钉用户ID：查看明细时精确定位到具体人员，避免同名混淆。</summary>
        public string? DDUserId { get; set; }
    }
}
