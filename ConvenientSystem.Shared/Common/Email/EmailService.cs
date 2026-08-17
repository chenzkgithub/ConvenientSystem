using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Entity.Email;
using FreeSql;

namespace ConvenientSystem.Shared.Common.Email
{
    /// <summary>
    /// 邮件发送服务：读取数据库 SMTP 配置，通过 System.Net.Mail 发送邮件。
    /// 授权码使用 AES 加密存储，发送前自动解密。
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<EmailService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 发送邮件
        /// </summary>
        /// <param name="recipients">收件人（多个用分号分隔）</param>
        /// <param name="subject">主题</param>
        /// <param name="body">正文</param>
        /// <param name="inlineImages">正文内嵌图片（CID 引用，兼容各家邮件客户端）</param>
        /// <returns>发送结果</returns>
        public async Task<EmailSendResult> SendAsync(string recipients, string subject, string body,
            IReadOnlyList<EmailInlineImage>? inlineImages = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var config = _fsql.Select<EmailConfigEntity>()
                    .Where(c => c.IsDefault && c.Enabled)
                    .First();

                if (config == null)
                {
                    return new EmailSendResult
                    {
                        Success = false,
                        ErrorMessage = "未配置邮件 SMTP 信息，请先在邮件配置中设置",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                // 解密授权码
                var password = AesEncryptHelper.Decrypt(config.Password);
                if (string.IsNullOrEmpty(password))
                {
                    return new EmailSendResult
                    {
                        Success = false,
                        ErrorMessage = "邮件授权码解密失败，请重新配置 SMTP 密码",
                        CostMs = (int)sw.ElapsedMilliseconds
                    };
                }

                using var client = new SmtpClient(config.SmtpServer, config.SmtpPort)
                {
                    EnableSsl = config.EnableSsl,
                    Credentials = new NetworkCredential(config.Account, password),
                    Timeout = 15000
                };

                var message = new MailMessage
                {
                    From = new MailAddress(config.Account, config.FromName),
                    Subject = subject
                };

                if (inlineImages is { Count: > 0 } && IsHtmlContent(body))
                {
                    // 内嵌图片：HTML 正文放入 AlternateView，图片以 LinkedResource 挂载（正文里 cid:ContentId 引用），
                    // 此时不再设置 Body，避免正文重复两份
                    var view = AlternateView.CreateAlternateViewFromString(body, null, "text/html");
                    foreach (var img in inlineImages)
                    {
                        var res = new LinkedResource(new MemoryStream(img.Data), "image/jpeg")
                        {
                            ContentId = img.ContentId,
                            TransferEncoding = TransferEncoding.Base64
                        };
                        view.LinkedResources.Add(res);
                    }
                    message.AlternateViews.Add(view);
                }
                else
                {
                    message.Body = body;
                    message.IsBodyHtml = IsHtmlContent(body);
                }

                // 解析多个收件人（分号/逗号分隔）
                var addresses = recipients
                    .Split(new[] { ';', ',', '；', '，' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrEmpty(a));

                foreach (var addr in addresses)
                {
                    message.To.Add(addr);
                }

                await client.SendMailAsync(message);

                sw.Stop();
                _logger.LogInformation("邮件发送成功 -> {Recipients}，耗时 {CostMs}ms", recipients, sw.ElapsedMilliseconds);

                return new EmailSendResult
                {
                    Success = true,
                    CostMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "邮件发送失败 -> {Recipients}", recipients);
                return new EmailSendResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CostMs = (int)sw.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// 自动检测内容是否为 HTML 格式
        /// </summary>
        private static bool IsHtmlContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return false;
            var htmlPatterns = new[] { "<html", "<body", "<div", "<table", "<p>", "<p ", "<br", "<h1", "<h2", "<h3", "<style", "<!doctype" };
            var lower = content.ToLower();
            foreach (var pattern in htmlPatterns)
            {
                if (lower.Contains(pattern)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 邮件发送结果
    /// </summary>
    public class EmailSendResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
    }
}
