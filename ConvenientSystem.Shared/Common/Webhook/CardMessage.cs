using System.Text;
using System.Text.Json;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 消息类型枚举。
    /// </summary>
    public enum MessageType
    {
        /// <summary>纯文本消息</summary>
        Text,

        /// <summary>富文本卡片消息（支持钉钉、企业微信、飞书）</summary>
        Card
    }

    /// <summary>
    /// 富文本卡片消息模板系统。
    /// 支持跨平台（钉钉 / 企业微信 / 飞书）卡片消息发送。
    /// </summary>
    public class CardMessage
    {
        /// <summary>卡片标题</summary>
        public string? Title { get; set; }

        /// <summary>卡片副标题</summary>
        public string? Subtitle { get; set; }

        /// <summary>卡片内容（支持 Markdown）</summary>
        public string? Content { get; set; }

        /// <summary>卡片颜色（十六进制，如 #FF5722）</summary>
        public string Color { get; set; } = "#FF5722";

        /// <summary>卡片按钮列表</summary>
        public List<CardButton> Buttons { get; set; } = new();

        /// <summary>卡片字段列表（可选）</summary>
        public List<CardField> Fields { get; set; } = new();

        /// <summary>转换为钉钉卡片 JSON（markdown 类型）。</summary>
        public string ToDingTalkMarkdown()
        {
            var md = new StringBuilder();
            if (!string.IsNullOrEmpty(Title))
                md.AppendLine($"# {Title}");
            if (!string.IsNullOrEmpty(Subtitle))
                md.AppendLine($"## {Subtitle}");
            if (!string.IsNullOrEmpty(Content))
                md.AppendLine(Content);

            foreach (var field in Fields)
                md.AppendLine($"- **{field.Label}**: {field.Value}");

            if (Buttons.Count > 0)
            {
                md.AppendLine("\n**操作**:");
                foreach (var btn in Buttons)
                    md.AppendLine($"- [{btn.Text}]({btn.Url})");
            }

            var payload = new
            {
                msgtype = "markdown",
                markdown = new { title = Title ?? "消息", text = md.ToString() }
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>转换为企业微信卡片 JSON（textcard 类型）。</summary>
        public string ToWeComCard()
        {
            var content = new StringBuilder();
            if (!string.IsNullOrEmpty(Content))
                content.Append(Content);

            foreach (var field in Fields)
                content.Append($"\n{field.Label}：{field.Value}");

            var btns = Buttons.Select(b => new { text = b.Text, url = b.Url }).ToList();

            var payload = new
            {
                msgtype = "textcard",
                textcard = new
                {
                    title = Title ?? "消息",
                    description = Subtitle ?? "",
                    url = Buttons.FirstOrDefault()?.Url ?? "",
                    btns = btns
                }
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>转换为飞书卡片 JSON（interactive 类型）。</summary>
        public string ToFeishuCard()
        {
            var elements = new List<object>();

            if (!string.IsNullOrEmpty(Title))
                elements.Add(new { tag = "markdown", content = $"**{Title}**" });

            if (!string.IsNullOrEmpty(Subtitle))
                elements.Add(new { tag = "markdown", content = $"_{Subtitle}_" });

            if (!string.IsNullOrEmpty(Content))
                elements.Add(new { tag = "markdown", content = Content });

            if (Fields.Count > 0)
            {
                var fieldMd = string.Join("\n", Fields.Select(f => $"- **{f.Label}**: {f.Value}"));
                elements.Add(new { tag = "markdown", content = fieldMd });
            }

            var payload = new
            {
                msg_type = "interactive",
                content = JsonSerializer.Serialize(new
                {
                    type = "template",
                    data = new
                    {
                        template_id = "AAqsqJe7OgSCZ", // 飞书卡片模板 ID（需替换为实际模板）
                        template_variable = new
                        {
                            title = Title ?? "消息",
                            content = string.Join("\n", elements.Select(e => JsonSerializer.Serialize(e))),
                            button_text = Buttons.FirstOrDefault()?.Text ?? "查看",
                            button_url = Buttons.FirstOrDefault()?.Url ?? ""
                        }
                    }
                })
            };
            return JsonSerializer.Serialize(payload);
        }
    }

    /// <summary>卡片按钮。</summary>
    public class CardButton
    {
        /// <summary>按钮显示文本</summary>
        public string Text { get; set; } = "";

        /// <summary>点击按钮跳转的 URL</summary>
        public string Url { get; set; } = "";

        /// <summary>可选：按钮类型（primary / default）</summary>
        public string Type { get; set; } = "primary";
    }

    /// <summary>卡片字段。</summary>
    public class CardField
    {
        /// <summary>字段标签</summary>
        public string Label { get; set; } = "";

        /// <summary>字段值</summary>
        public string Value { get; set; } = "";

        public CardField() { }

        public CardField(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}
