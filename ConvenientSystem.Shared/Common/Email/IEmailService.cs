namespace ConvenientSystem.Shared.Common.Email
{
    /// <summary>
    /// 邮件发送服务：读取数据库 SMTP 配置发送邮件。
    /// </summary>
    public interface IEmailService
    {
        /// <summary>发送邮件</summary>
        /// <param name="recipients">收件人（多个用分号/逗号分隔，支持中文标点）</param>
        /// <param name="subject">主题</param>
        /// <param name="body">正文（自动识别 HTML）</param>
        /// <param name="inlineImages">正文内嵌图片（HTML 中用 cid:ContentId 引用；为空则纯 HTML 发送）</param>
        /// <returns>发送结果（不抛异常，失败信息在结果中）</returns>
        Task<EmailSendResult> SendAsync(string recipients, string subject, string body,
            IReadOnlyList<EmailInlineImage>? inlineImages = null);
    }

    /// <summary>邮件正文内嵌图片：以 CID 方式嵌入 HTML 正文（兼容性优于外链图片，不受收件方拦截外链影响）</summary>
    public class EmailInlineImage
    {
        /// <summary>内容 Id：HTML 中通过 src="cid:ContentId" 引用</summary>
        public string ContentId { get; set; } = string.Empty;

        /// <summary>图片数据（JPEG）</summary>
        public byte[] Data { get; set; } = [];
    }
}
