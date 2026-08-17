using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using Hangfire;
using Microsoft.AspNetCore.Http;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票玩法规则服务实现：读写 LotteryRule 版本表。
    /// 页面拿到的奖级规则即判奖实际依据（库内无生效版本时为内置兜底规则），
    /// 保证「对照表展示的规则」与「实际判奖用的规则」永远是同一份数据。
    /// </summary>
    public class LotteryRuleService : ILotteryRuleService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public LotteryRuleService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        public LotteryRuleViewDto GetView(string type)
        {
            var t = LotteryTypes.Normalize(type);
            var positional = LotteryTypes.IsPositional(t);
            var zones = LotteryTypes.GetPickZones(t);
            var front = zones.Where(z => z.Source == "front").ToList();
            var back = zones.Where(z => z.Source == "back").ToList();

            var active = _fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t && r.Status == LotteryRuleStatus.Active)
                .OrderByDescending(r => r.Version)
                .First();
            var pending = _fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t && r.Status == LotteryRuleStatus.Pending)
                .OrderByDescending(r => r.Version)
                .First();

            // 判奖走的是 LotteryRuleCache（生效版本，异常回落内置），展示口径与之保持一致
            var current = ToVersionDto(t, active) ?? DefaultVersion(t);

            return new LotteryRuleViewDto
            {
                LotteryType = t,
                TypeName = LotteryTypes.GetName(t),
                Positional = positional,
                FrontTotal = positional ? zones.Count : front.Sum(z => z.Pick),
                BackTotal = positional ? 0 : back.Sum(z => z.Pick),
                FrontLabel = positional ? "号码" : ZoneLabel(front),
                BackLabel = positional ? string.Empty : ZoneLabel(back),
                Current = current,
                UsingDefault = current.Version == 0,
                Pending = ToVersionDto(t, pending),
            };
        }

        public List<LotteryRuleVersionDto> GetVersions(string type)
        {
            var t = LotteryTypes.Normalize(type);
            return _fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t)
                .OrderByDescending(r => r.Version)
                .ToList()
                .Select(r => ToVersionDto(t, r)!)
                .ToList();
        }

        public bool Review(LotteryRuleReviewDto dto)
        {
            var row = _fsql.Select<LotteryRuleEntity>().Where(r => r.Id == dto.Id).First()
                ?? throw new NotFoundException("规则版本不存在");
            if (row.Status != LotteryRuleStatus.Pending)
                throw new BadRequestException("该版本不在待审核状态");

            var now = DateTime.Now;
            var account = _currentUser.Account ?? "未知";

            if (!dto.Approve)
            {
                _fsql.Update<LotteryRuleEntity>()
                    .Set(r => r.Status, LotteryRuleStatus.Rejected)
                    .Set(r => r.ReviewedBy, account)
                    .Set(r => r.Remark, Remark(row.Remark, dto.Remark))
                    .Where(r => r.Id == row.Id)
                    .ExecuteAffrows();
                return true;
            }

            // 同一彩种只能有一个生效版本，旧版本标记为已被替代
            _fsql.Update<LotteryRuleEntity>()
                .Set(r => r.Status, LotteryRuleStatus.Replaced)
                .Where(r => r.LotteryType == row.LotteryType && r.Status == LotteryRuleStatus.Active)
                .ExecuteAffrows();

            _fsql.Update<LotteryRuleEntity>()
                .Set(r => r.Status, LotteryRuleStatus.Active)
                .Set(r => r.EffectiveAt, now)
                .Set(r => r.ReviewedBy, account)
                .Set(r => r.Remark, Remark(row.Remark, dto.Remark))
                .Where(r => r.Id == row.Id)
                .ExecuteAffrows();

            // 判奖缓存必须立刻失效，否则新规则最多要等 5 分钟才生效
            LotteryRuleCache.Invalidate(row.LotteryType);
            return true;
        }

        public string CrawlNow(string type)
        {
            var t = LotteryTypes.Normalize(type);
            return BackgroundJob.Enqueue<LotteryRuleCrawlJob>(job => job.CrawlAsync(t, default));
        }

        /// <summary>内置兜底规则的展示版本（版本号 0，表示当前判奖不依赖库内数据）</summary>
        private static LotteryRuleVersionDto DefaultVersion(string type) => new()
        {
            Id = 0,
            Version = 0,
            Status = LotteryRuleStatus.Active,
            StatusText = "内置规则",
            Grades = LotteryRuleDefaults.Get(type).Grades,
            Remark = "尚未抓取到官网条文，当前使用内置规则判奖",
        };

        private static LotteryRuleVersionDto? ToVersionDto(string type, LotteryRuleEntity? row)
        {
            if (row == null) return null;
            return new LotteryRuleVersionDto
            {
                Id = row.Id,
                Version = row.Version,
                Status = row.Status,
                StatusText = StatusText(row.Status),
                SourceUrl = row.SourceUrl,
                RuleText = row.RuleText,
                // GradeJson 损坏时退回内置规则，弹窗不至于画出空对照表
                Grades = LotteryRuleCache.Deserialize(row.GradeJson) ?? LotteryRuleDefaults.Get(type).Grades,
                CrawledAt = row.CrawledAt,
                EffectiveAt = row.EffectiveAt,
                ReviewedBy = row.ReviewedBy,
                Remark = row.Remark,
            };
        }

        private static string StatusText(byte status) => status switch
        {
            LotteryRuleStatus.Active => "生效中",
            LotteryRuleStatus.Pending => "待审核",
            LotteryRuleStatus.Rejected => "已驳回",
            _ => "已被新版替代",
        };

        /// <summary>审核备注：填了就覆盖抓取时写入的差异说明，没填则保留原文</summary>
        private static string? Remark(string? origin, string? input)
            => string.IsNullOrWhiteSpace(input) ? origin : input.Trim();

        /// <summary>分区名（红球区/蓝球区 去掉"区"字，前区/后区 保持原样）</summary>
        private static string ZoneLabel(List<LotteryZoneDto> zones)
        {
            var label = zones.FirstOrDefault()?.Label ?? string.Empty;
            return label.Length > 2 && label.EndsWith('区') ? label[..^1] : label;
        }
    }
}
