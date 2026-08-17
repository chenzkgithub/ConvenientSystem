using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 应用启动时注册彩票开奖相关定时任务：
    /// - 随机生成选号 13:00（当天开奖的每个彩种为每个启用用户生成 10 注随机号码）
    /// - 开奖数据爬取 22:00（各彩种开奖时间 21:15~21:25，统一固定时间拉取）
    /// - 当天开奖结果邮件汇总 22:10（爬取完成后，整合当天全部开奖彩种发一封邮件）
    /// - 奖级明细补拉 23:30（官方奖金数据常晚于开奖号码发布，主爬抓不到时当晚补一次）
    /// - 玩法规则抓取 06:00（官网条文变动极少，每天一次即可，抓到差异只入待审不自动改判奖）
    /// </summary>
    public class LotteryStartupCompensator
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<LotteryStartupCompensator> _logger;

        public LotteryStartupCompensator(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<LotteryStartupCompensator> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 启动时注册全部彩种的数据爬取定时任务及每日开奖结果邮件汇总任务；
        /// 历史开奖若缺官方中奖明细（选号验证需要），后台补爬一次
        /// </summary>
        public void Compensate()
        {
            // Cron: 分 时 日 月 星期；开奖数据爬取统一固定每天 22:00 执行（各彩种开奖 21:15~21:25 后数据已发布）
            // 23:30 再补拉一轮最近几期：奖级注数与单注奖金的官方发布常晚于开奖号码，主爬时可能仍为空
            foreach (var t in LotteryTypes.All)
            {
                Register(t, "0 22 * * *");
                RegisterBackfill(t, "30 23 * * *");
                RegisterRule(t, "0 6 * * *");
            }

            // 每天固定 13:00：为当天开奖的每个彩种、每个启用用户随机生成 10 注选号记录
            RecurringJob.AddOrUpdate<LotteryRandomPickJob>(
                "随机生成彩种号码",
                job => job.DailyRandomPickAsync(default),
                "0 13 * * *",
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });
            _logger.LogInformation("彩票启动补偿：随机生成彩种号码定时任务已注册，Cron=0 13 * * *");

            // 每天固定 22:10：判断当天哪些彩种开奖，库中缺失则补拉最新一期，整合为一封邮件发送
            RecurringJob.AddOrUpdate<LotteryResultNotifyJob>(
                "当天开奖结果邮件汇总",
                job => job.DailyNotifyAsync(default),
                "10 22 * * *",
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });
            _logger.LogInformation("彩票启动补偿：当天开奖结果邮件汇总定时任务已注册，Cron=10 22 * * *");

            EnqueuePrizeBackfill();
        }

        /// <summary>
        /// 存在缺官网通告数据的开奖记录时，后台入队回填爬取（仅限最早选号日期之后的期，更早的期无人验证）：
        /// 缺失口径按彩种——全部彩种查缺中奖明细；体彩另查缺通告 PDF 链接，双色球另查缺中奖地区（福彩3D 官网均不提供）
        /// </summary>
        private void EnqueuePrizeBackfill()
        {
            var oldestPick = _fsql.Select<LotteryRecordEntity>().Min(r => r.CreatedAt);
            if (oldestPick == default) return; // 无选号记录则无需验证，不补爬
            var since = oldestPick.Date;

            var enqueued = 0;
            foreach (var t in LotteryTypes.All)
            {
                var type = t;
                var missing = type switch
                {
                    // 体彩（大乐透/排列五）：中奖明细或通告 PDF 链接缺失
                    LotteryTypes.DLT or LotteryTypes.PL5 => _fsql.Select<LotteryDrawEntity>()
                        .Where(d => d.LotteryType == type && d.DrawDate >= since
                            && (d.PrizeDetail == null || d.PrizeDetail == ""
                                || d.NoticeUrl == null || d.NoticeUrl == "")).Any(),
                    // 双色球：中奖明细或中奖地区缺失
                    LotteryTypes.SSQ => _fsql.Select<LotteryDrawEntity>()
                        .Where(d => d.LotteryType == type && d.DrawDate >= since
                            && (d.PrizeDetail == null || d.PrizeDetail == ""
                                || d.PrizeArea == null || d.PrizeArea == "")).Any(),
                    // 福彩3D：官网不提供地区/PDF，仅查中奖明细
                    _ => _fsql.Select<LotteryDrawEntity>()
                        .Where(d => d.LotteryType == type && d.DrawDate >= since
                            && (d.PrizeDetail == null || d.PrizeDetail == "")).Any(),
                };
                if (!missing) continue;

                enqueued++;
                BackgroundJob.Enqueue<LotteryDrawCrawlJob>(
                    job => job.CrawlAsync(type, 900, true, since, default));
            }
            if (enqueued > 0)
                _logger.LogInformation("存在缺官网通告数据的历史开奖，已入队 {Count} 个回填爬取（覆盖至 {Since}）",
                    enqueued, since.ToString("yyyy-MM-dd"));
        }

        private void Register(string type, string cronExpression)
        {
            var jobId = $"{LotteryTypes.GetName(type)}开奖数据爬取";

            RecurringJob.AddOrUpdate<LotteryDrawCrawlJob>(
                jobId,
                job => job.CrawlAsync(type, 900, false, null, default),
                cronExpression,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });

            _logger.LogInformation("彩票启动补偿：{JobId} 定时爬取任务已注册，Cron={Cron}", jobId, cronExpression);
        }

        /// <summary>
        /// 注册当日奖级明细补拉任务：只重拉最近几期，把主爬时官方尚未发布的奖金/通告数据补齐。
        /// 回填不会覆盖库内已有值（源数据为空时保留原值），重复跑安全。
        /// </summary>
        private void RegisterBackfill(string type, string cronExpression)
        {
            var jobId = $"{LotteryTypes.GetName(type)}奖级明细补拉";

            RecurringJob.AddOrUpdate<LotteryDrawCrawlJob>(
                jobId,
                job => job.BackfillRecentAsync(type, 2, default),
                cronExpression,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });

            _logger.LogInformation("彩票启动补偿：{JobId} 定时补拉任务已注册，Cron={Cron}", jobId, cronExpression);
        }

        /// <summary>
        /// 注册玩法规则抓取任务：每天拉一次官网条文，内容无变化只更新抓取时间；
        /// 有变化则入库为待审版本，需用户在页面确认后才切为判奖依据（不自动改变判奖行为）。
        /// </summary>
        private void RegisterRule(string type, string cronExpression)
        {
            var jobId = $"{LotteryTypes.GetName(type)}玩法规则抓取";

            RecurringJob.AddOrUpdate<LotteryRuleCrawlJob>(
                jobId,
                job => job.CrawlAsync(type, default),
                cronExpression,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });

            _logger.LogInformation("彩票启动补偿：{JobId} 定时抓取任务已注册，Cron={Cron}", jobId, cronExpression);
        }
    }
}
