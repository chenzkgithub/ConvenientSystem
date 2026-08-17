using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 企业微信群机器人。UseCard=false 发 text，UseCard=true 发 template_card（文本通知型卡片）。
    /// 鉴权 key 已包含在 WebhookUrl 中，无需额外加签。
    /// 文档：https://developer.work.weixin.qq.com/document/path/91770
    /// </summary>
    public class WeComProvider : IWebhookProvider
    {
        private readonly ILogger<WeComProvider> _logger;
        private readonly IFreeSql _configDb;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public string ProviderType => "wecom";

        public WeComProvider(ILogger<WeComProvider> logger, [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        /// <summary>从 SysConfig 表读取公开应用 URL，DB 不可用时返回 null。</summary>
        private string? GetPublicAppUrl()
        {
            try
            {
                var entity = _configDb.Select<SysConfigEntity>()
                    .Where(e => e.ConfigKey == "AppSettings.PublicAppUrl")
                    .First();
                return string.IsNullOrEmpty(entity?.ConfigValue) ? null : entity.ConfigValue;
            }
            catch { return null; }
        }

        public async Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                object payloadObj;
                if (cfg.UseCard)
                {
                    // 富文本卡片：企业微信 template_card / text_notice
                    payloadObj = BuildTemplateCard(title, content);
                }
                else
                {
                    var text = string.IsNullOrEmpty(title) ? content : $"【{title}】\n{content}";
                    payloadObj = new { msgtype = "text", text = new { content = text } };
                }
                var payload = JsonSerializer.Serialize(payloadObj);

                var resp = await _http.PostAsync(cfg.WebhookUrl,
                    new StringContent(payload, Encoding.UTF8, "application/json"));
                sw.Stop();

                var body = await resp.Content.ReadAsStringAsync();
                var errcode = DingTalkProvider.TryGetInt(body, "errcode");
                if (errcode == 0)
                    return WebhookSendResult.Ok((int)sw.ElapsedMilliseconds);
                return WebhookSendResult.Fail($"企业微信返回：{body}", (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "企业微信机器人推送异常");
                return WebhookSendResult.Fail(ex.Message, (int)sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// 构建企业微信 template_card（文本通知型）。
        /// 解析 content 中的副标题、彩种期号、开奖号码、中奖汇总、PDF 链接，
        /// 分别填入卡片主标题/副标题/高亮区/副文本/跳转列表，避免卡片只显示一句摘要。
        /// 卡片整体点击跳转到应用内开奖汇总详情页，PDF 通过底部 jump_list 按钮进入。
        /// </summary>
        private object BuildTemplateCard(string? title, string? content)
        {
            var mainTitle = Truncate(StripMarkdown(title), 50);
            var parsed = ParseCardContent(content);

            var emphasis = string.IsNullOrEmpty(parsed.HighlightContent)
                ? null
                : new { title = Truncate(parsed.HighlightTitle, 20), content = Truncate(parsed.HighlightContent, 100) };

            var jumps = parsed.PdfLinks.Select(l => new { type = 1, url = l.Url, title = Truncate(l.Title, 50) }).ToList();

            var baseUrl = GetPublicAppUrl()?.TrimEnd('/') ?? "http://127.0.0.1:51943";
            var summaryDate = ParseDateFromSubtitle(parsed.Subtitle) ?? DateTime.Today;
            var detailUrl = $"{baseUrl}/#/lottery-result-summary?standalone=1&date={summaryDate:yyyy-MM-dd}";

            var card = new Dictionary<string, object>
            {
                ["msgtype"] = "template_card",
                ["template_card"] = new Dictionary<string, object>
                {
                    ["card_type"] = "text_notice",
                    ["source"] = new { desc = "ConvenientSystem", desc_color = 0 },
                    ["main_title"] = new
                    {
                        title = string.IsNullOrEmpty(mainTitle) ? "消息通知" : mainTitle,
                        desc = string.IsNullOrEmpty(parsed.Subtitle) ? " " : Truncate(parsed.Subtitle, 200)
                    },
                    // 卡片整体点击进入详情页看完整中奖明细表格
                    ["card_action"] = new { type = 1, url = detailUrl }
                }
            };

            var templateCard = (Dictionary<string, object>)card["template_card"];
            if (emphasis != null)
                templateCard["emphasis_content"] = emphasis;
            if (!string.IsNullOrEmpty(parsed.Summary))
                templateCard["sub_title_text"] = Truncate(parsed.Summary, 300);
            if (jumps.Count > 0)
                templateCard["jump_list"] = jumps;

            return card;
        }

        /// <summary>从副标题（如 "2026-08-12 · 当天开奖..."）中提取汇总日期。</summary>
        private static DateTime? ParseDateFromSubtitle(string? subtitle)
        {
            if (string.IsNullOrWhiteSpace(subtitle)) return null;
            var match = Regex.Match(subtitle.Trim(), @"^(\d{4}-\d{2}-\d{2})");
            if (match.Success
                && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                return d.Date;
            return null;
        }

        /// <summary>从 Markdown 内容中提取卡片所需字段。</summary>
        private static (string Subtitle, string HighlightTitle, string HighlightContent, string Summary, List<(string Title, string Url)> PdfLinks)
            ParseCardContent(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return ("", "", "", "", new List<(string, string)>());

            var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

            // 副标题：通常是被 ** 包裹的日期说明行（紧跟在 ## 标题后面）
            var subtitle = "";
            var subtitleMatch = lines.Select(l => Regex.Match(l, @"^\*\*(.+?)\*\*$")).FirstOrDefault(m => m.Success);
            if (subtitleMatch != null)
                subtitle = StripMarkdown(subtitleMatch.Groups[1].Value);

            // 高亮："共 X 注中奖..." 的汇总行
            var highlightTitle = "中奖汇总";
            var highlightContent = "";
            var winLine = lines.FirstOrDefault(l => l.Contains("🎉") && l.Contains("中奖"));
            if (!string.IsNullOrEmpty(winLine))
            {
                highlightContent = StripMarkdown(winLine).Replace("🎉", "").Trim();
                if (string.IsNullOrEmpty(highlightContent))
                    highlightContent = "";
            }

            // 摘要：提取所有 "### 彩种 第X期"、对应开奖号码、该彩种下每个用户的选号中奖结果，
            // 以及该彩种对应的官网通告 PDF 链接（用彩种名+期号命名，避免多个彩种都显示同一个标题）。
            var summaries = new List<string>();
            var links = new List<(string Title, string Url)>();
            for (var i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"^###\s*(.+?)\s+第(\d+)期\s*$");
                if (!m.Success) continue;
                var typeName = m.Groups[1].Value.Trim();
                var issue = $"第{m.Groups[2].Value}期";
                var drawLabel = $"{typeName}{issue}";

                // 下一行可能是开奖日期，再下一行是开奖号码
                var numberLine = "";
                for (var j = i + 1; j < Math.Min(i + 4, lines.Count); j++)
                {
                    var nm = Regex.Match(lines[j], @"^\*\*开奖号码：\*\*\s*`?(.+?)`?\s*$");
                    if (nm.Success)
                    {
                        numberLine = nm.Groups[1].Value.Trim();
                        break;
                    }
                }

                // 收集该彩种下 "- 用户名：`选号` → 命中 | 中奖结果 | 奖金" 的行，以及 PDF 链接
                var userResults = new List<string>();
                for (var j = i + 1; j < lines.Count && !lines[j].StartsWith("### "); j++)
                {
                    var um = Regex.Match(lines[j], @"^-\s*(.+?)：``?(.+?)``?\s*→\s*(.+?)\s*\|\s*(.+?)\s*\|\s*(.+)$");
                    if (um.Success)
                    {
                        var userName = um.Groups[1].Value.Trim();
                        var prize = um.Groups[4].Value.Trim();
                        userResults.Add($"{userName}：{prize}");
                        continue;
                    }

                    var lm = Regex.Match(lines[j], @"\[([^\]]+)\]\((https?://[^\s)]+)\)");
                    if (lm.Success)
                    {
                        var linkTitle = StripMarkdown(lm.Groups[1].Value);
                        // 把通用标题替换成带彩种期号的标题，便于区分
                        if (linkTitle.Contains("官网通告") || linkTitle.Contains("PDF"))
                            linkTitle = $"{drawLabel} 官网通告";
                        links.Add((linkTitle, lm.Groups[2].Value));
                    }
                }

                var summary = string.IsNullOrEmpty(numberLine)
                    ? drawLabel
                    : $"{drawLabel} {numberLine}";
                if (userResults.Count > 0)
                    summary += "（" + string.Join("，", userResults) + "）";
                summaries.Add(StripMarkdown(summary));
            }
            var summaryText = summaries.Count > 0
                ? string.Join("；", summaries)
                : "";

            return (subtitle, highlightTitle, highlightContent, summaryText, links);
        }

        /// <summary>移除常见 Markdown 标记，避免卡片描述里出现原始语法。</summary>
        private static string StripMarkdown(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";
            var s = markdown;
            // 标题 #
            s = Regex.Replace(s, @"^#{1,6}\s*", "", RegexOptions.Multiline);
            // 粗体 / 斜体
            s = Regex.Replace(s, @"(\*\*\*|\*\*|\*|__|_)(.+?)\1", "$2");
            // 行内代码 / 代码块
            s = Regex.Replace(s, @"`{3}[\s\S]*?`{3}", "");
            s = Regex.Replace(s, @"`([^`]+)`", "$1");
            // 链接 [text](url) -> text
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
            // 图片 ![alt](url) -> [图片]
            s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]+\)", "[图片]");
            // 引用块 >
            s = Regex.Replace(s, @"^>\s*", "", RegexOptions.Multiline);
            // 列表标记 - * 1.
            s = Regex.Replace(s, @"^[\s]*[-*+]\s+|\d+\.\s+", "", RegexOptions.Multiline);
            // 水平线
            s = Regex.Replace(s, @"^\s*[-]{3,}\s*$", "", RegexOptions.Multiline);
            // 多余空行
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        /// <summary>按字符数截断文本，优先保留完整单词/行。</summary>
        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value[..maxLength].TrimEnd() + "…";
        }
    }
}
