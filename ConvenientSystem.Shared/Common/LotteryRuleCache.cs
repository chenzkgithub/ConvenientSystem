using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 生效玩法规则缓存：判奖是热路径（首页/邮件/验奖会逐注调用），不能每注都查库。
    /// 按彩种缓存 Status=1 版本的奖级规则，TTL 到期或审核启用新版本后失效重取；
    /// 库内无生效版本或 JSON 解析失败时回落内置兜底规则，保证抓取失败也能照旧判奖。
    /// </summary>
    public static class LotteryRuleCache
    {
        private static readonly ConcurrentDictionary<string, (DateTime Expire, LotteryRuleDto Rule)> _cache = new();

        /// <summary>缓存有效期：规则变动本就极少，5 分钟足够摊掉逐注查库开销</summary>
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>取彩种当前判奖依据的规则（生效版本，无则内置兜底）</summary>
        public static LotteryRuleDto Get(IFreeSql fsql, string type)
        {
            type = LotteryTypes.Normalize(type);
            if (_cache.TryGetValue(type, out var hit) && hit.Expire > DateTime.Now) return hit.Rule;

            var rule = Load(fsql, type);
            _cache[type] = (DateTime.Now.Add(Ttl), rule);
            return rule;
        }

        /// <summary>清缓存（审核启用新版本后调用；type 为空时清全部彩种）</summary>
        public static void Invalidate(string? type = null)
        {
            if (string.IsNullOrEmpty(type)) _cache.Clear();
            else _cache.TryRemove(LotteryTypes.Normalize(type), out _);
        }

        /// <summary>奖级规则 → JSON（存 LotteryRule.GradeJson）</summary>
        public static string Serialize(List<LotteryGradeRuleDto> grades) => JsonSerializer.Serialize(grades);

        /// <summary>JSON → 奖级规则（按 Order 升序）；内容为空或格式非法时返回 null</summary>
        public static List<LotteryGradeRuleDto>? Deserialize(string? gradeJson)
        {
            if (string.IsNullOrWhiteSpace(gradeJson)) return null;
            try
            {
                var list = JsonSerializer.Deserialize<List<LotteryGradeRuleDto>>(gradeJson, ReadOptions);
                if (list == null || list.Count == 0) return null;
                return list.OrderBy(g => g.Order).ToList();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static LotteryRuleDto Load(IFreeSql fsql, string type)
        {
            try
            {
                var row = fsql.Select<LotteryRuleEntity>()
                    .Where(r => r.LotteryType == type && r.Status == LotteryRuleStatus.Active)
                    .OrderByDescending(r => r.Version)
                    .First();
                var grades = LotteryRuleCache.Deserialize(row?.GradeJson);
                if (grades != null)
                    return new LotteryRuleDto { LotteryType = type, Grades = grades };
            }
            catch (Exception)
            {
                // 规则表不可用（未建表/连接异常）不能拖垮判奖，静默回落内置规则
            }
            return LotteryRuleDefaults.Get(type);
        }
    }
}
