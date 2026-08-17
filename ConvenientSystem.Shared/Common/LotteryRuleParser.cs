using ConvenientSystem.Shared.Model.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 官网玩法规则条文解析：HTML → 纯文本 → 结构化奖级规则。
    /// 池选型（DLT/SSQ）从"中奖"章条文动态提取奖级与命中条件，条文的"至少"语义落成 Conds（或关系）；
    /// 位置型（PL5/FC3D）按位判奖逻辑固定在代码里，解析只补充官方条文原文与单注固定奖金。
    /// 解析结果通不过合理性校验时返回 null，由调用方回落内置兜底规则，绝不拿半成品规则去判奖。
    /// </summary>
    public static class LotteryRuleParser
    {
        private static readonly Regex ScriptTag = new(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase);
        private static readonly Regex StyleTag = new(@"<style[\s\S]*?</style>", RegexOptions.IgnoreCase);
        private static readonly Regex BlockEndTag = new(@"</p>|<br\s*/?>|</div>|</li>|</tr>|</h\d>", RegexOptions.IgnoreCase);
        private static readonly Regex AnyTag = new(@"<[^>]+>");
        private static readonly Regex MultiSpace = new(@"\s+");

        /// <summary>行首编号（（一）/（1）/1./1、），奖级名前的排版前缀，比对奖级名时先剥掉</summary>
        private static readonly Regex LeadingNumber = new(@"^(?:[（(]\s*[0-9一二三四五六七八九十]{1,3}\s*[)）]|[0-9]{1,2}\s*[.、]|[一二三四五六七八九十]{1,3}\s*、)\s*");

        /// <summary>奖级条文行：「奖级名：条文」（奖级名 2-4 个汉字且以"奖"结尾，如 一等奖/福运奖）</summary>
        private static readonly Regex PoolGradeLine = new(@"^([\u4e00-\u9fa5]{2,4}奖)[：:]\s*(\S.*)$");

        /// <summary>并列命中条件的分隔（"，或者" / "，或与" / "，或"），切出的每段是一个独立的中奖条件</summary>
        private static readonly Regex OrSeparator = new(@"[，,]\s*或(?:者|与|)");

        /// <summary>条文中的区位命中个数，如「5个前区号码」「任意4个红色球号码」</summary>
        private static readonly Regex ZoneHitCount = new(@"(\d+)\s*个\s*(前区|后区|红色?球|蓝色?球)");

        /// <summary>单注固定奖金，如「单注奖金固定为3000元」「单注固定奖金100000元」「单注奖金为5000元」</summary>
        private static readonly Regex FixedMoneyText = new(@"单注(?:奖金固定为|固定奖金|奖金为)\s*([\d,]+)\s*元");

        /// <summary>附条件奖级标志（双色球福运奖仅在执行特别规定期间设立）</summary>
        private const string ConditionalMark = "执行特别规定期间";

        /// <summary>官网正文容器（中彩网规则文章），命中则只取正文段落，去掉导航与推荐位</summary>
        private const string BodyMark = "wz-xq";

        /// <summary>页脚起始标记，其后内容不属于条文</summary>
        private static readonly string[] FooterMarks = ["关于我们", "版权所有", "网站声明"];

        /// <summary>
        /// 规则页 HTML → 逐行纯文本（解码实体、压空白、去空行），供条文解析与页面展示
        /// </summary>
        public static string HtmlToText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var t = html;
            var body = t.IndexOf(BodyMark, StringComparison.OrdinalIgnoreCase);
            if (body > 0) t = t.Substring(body);

            t = ScriptTag.Replace(t, string.Empty);
            t = StyleTag.Replace(t, string.Empty);
            t = BlockEndTag.Replace(t, "\n");
            t = AnyTag.Replace(t, string.Empty);
            t = t.Replace("&nbsp;", " ").Replace("&#32;", " ").Replace("&ldquo;", "“").Replace("&rdquo;", "”")
                 .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&")
                 .Replace('\u00A0', ' ');

            var sb = new StringBuilder();
            foreach (var raw in t.Split('\n'))
            {
                var line = MultiSpace.Replace(raw, " ").Trim();
                if (line.Length == 0) continue;
                if (FooterMarks.Contains(line)) break;
                sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// 条文纯文本 → 结构化规则；解析不出可信奖级时返回 null（调用方回落内置兜底规则）
        /// </summary>
        public static LotteryRuleDto? Parse(string type, string? text)
        {
            type = LotteryTypes.Normalize(type);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => LeadingNumber.Replace(l.Trim(), string.Empty).Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var grades = LotteryTypes.IsPositional(type)
                ? ParsePositional(type, lines)
                : ParsePool(type, lines);
            if (grades == null) return null;

            return new LotteryRuleDto { LotteryType = type, Grades = grades };
        }

        /// <summary>池选型（DLT/SSQ）：从条文行动态提取奖级与命中条件，奖级顺序即条文出现顺序</summary>
        private static List<LotteryGradeRuleDto>? ParsePool(string type, List<string> lines)
        {
            var (frontPick, backPick) = GetPicks(type);
            var grades = new List<LotteryGradeRuleDto>();

            foreach (var line in lines)
            {
                var m = PoolGradeLine.Match(line);
                if (!m.Success) continue;
                // 同一奖级名在"奖金设置"章也有同格式行，只有含"相同"的才是中奖条件条文
                var content = m.Groups[2].Value;
                if (!content.Contains("相同")) continue;

                var name = m.Groups[1].Value;
                if (grades.Any(g => g.Grade == name)) continue;

                var conds = ParseConds(content, frontPick, backPick);
                if (conds.Count == 0) return null;

                grades.Add(new LotteryGradeRuleDto
                {
                    Grade = name,
                    SystemGrade = name,
                    Order = grades.Count + 1,
                    Match = LotteryMatchKind.Hit,
                    Conds = conds,
                    ConditionText = content,
                    Conditional = content.Contains(ConditionalMark),
                });
            }

            if (!IsPoolSane(grades, frontPick, backPick)) return null;
            FillMoney(grades, lines);
            return grades;
        }

        /// <summary>一条中奖条件条文 → 命中条件列表（"或者"并列的每段一条，取"至少命中个数"）</summary>
        private static List<LotteryHitCondDto> ParseConds(string content, int frontPick, int backPick)
        {
            var conds = new List<LotteryHitCondDto>();
            foreach (var clause in OrSeparator.Split(content))
            {
                var front = 0;
                var back = 0;
                var hasAny = false;
                // "全部相同"即整注命中，等于各区选号个数
                if (clause.Contains("全部相同"))
                {
                    front = frontPick;
                    back = backPick;
                    hasAny = true;
                }
                foreach (Match hit in ZoneHitCount.Matches(clause))
                {
                    if (!int.TryParse(hit.Groups[1].Value, out var n)) continue;
                    var zone = hit.Groups[2].Value;
                    if (zone.StartsWith("前区") || zone.StartsWith("红")) front = Math.Max(front, n);
                    else back = Math.Max(back, n);
                    hasAny = true;
                }
                if (!hasAny) continue;
                if (front > frontPick || back > backPick) return new List<LotteryHitCondDto>();
                if (conds.Any(c => c.Front == front && c.Back == back)) continue;
                conds.Add(new LotteryHitCondDto { Front = front, Back = back });
            }
            return conds;
        }

        /// <summary>池选型解析结果合理性校验：奖级数正常、首级为整注命中、每级都有条件</summary>
        private static bool IsPoolSane(List<LotteryGradeRuleDto> grades, int frontPick, int backPick)
        {
            if (grades.Count < 3 || grades.Count > 12) return false;
            if (grades.Any(g => g.Conds.Count == 0)) return false;
            return grades[0].Conds.Any(c => c.Front == frontPick && c.Back == backPick);
        }

        /// <summary>位置型（PL5/FC3D）：奖级与判定方式固定，只从条文补条件原文，缺任一奖级即视为解析失败</summary>
        private static List<LotteryGradeRuleDto>? ParsePositional(string type, List<string> lines)
        {
            var expect = LotteryRuleDefaults.Get(type).Grades;
            foreach (var g in expect)
            {
                // 中奖条件行含"相同"，奖金行含"单注...元"，两类行同为「奖级名：内容」格式
                var cond = FindGradeContent(lines, g.Grade, c => c.Contains("相同"));
                if (cond == null) return null;
                g.ConditionText = cond;
                g.FixedMoney = null;
                g.MoneyText = null;
            }
            FillMoney(expect, lines);
            // 奖金是位置型彩种的唯一奖金来源（官方明细里也是固定值），缺失即不可用
            return expect.All(g => g.FixedMoney.HasValue) ? expect : null;
        }

        /// <summary>从条文里补每个奖级的单注固定奖金：同一金额只出现一档时填 FixedMoney，多档时只留原文</summary>
        private static void FillMoney(List<LotteryGradeRuleDto> grades, List<string> lines)
        {
            foreach (var g in grades)
            {
                var content = FindGradeContent(lines, g.Grade, c => FixedMoneyText.IsMatch(c));
                if (content == null) continue;

                var money = FixedMoneyText.Matches(content)
                    .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(",", string.Empty), out var v) ? v : -1m)
                    .Where(v => v > 0)
                    .Distinct()
                    .ToList();
                g.MoneyText = content;
                // 大乐透三~七等奖按奖池是否达 8 亿元分两档，无法定值，只保留原文交官方明细定奖金
                g.FixedMoney = money.Count == 1 ? money[0] : null;
            }
        }

        /// <summary>查找「奖级名：内容」格式且内容满足条件的首行，返回去掉奖级名前缀后的条文内容</summary>
        private static string? FindGradeContent(List<string> lines, string grade, Func<string, bool> match)
        {
            foreach (var line in lines)
            {
                if (!line.StartsWith(grade, StringComparison.Ordinal)) continue;
                var rest = line.Substring(grade.Length);
                if (rest.Length == 0 || !"：:，,".Contains(rest[0])) continue;
                var content = rest.Substring(1).Trim();
                if (content.Length > 0 && match(content)) return content;
            }
            return null;
        }

        /// <summary>彩种前后区选号个数（"全部相同"与条件上限校验的基准）</summary>
        private static (int Front, int Back) GetPicks(string type)
        {
            var zones = LotteryTypes.GetPickZones(type);
            var front = zones.Where(z => z.Source == "front").Sum(z => z.Pick);
            var back = zones.Where(z => z.Source == "back").Sum(z => z.Pick);
            return (front, back);
        }
    }
}
