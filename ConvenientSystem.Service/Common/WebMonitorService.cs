using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 网站/API 监控业务服务实现：目标配置管理 + 探测日志查询 + 手动立即检测（复用巡检 Job 的探测逻辑）
    /// </summary>
    public class WebMonitorService : IWebMonitorService
    {
        private readonly IFreeSql _fsql;
        private readonly WebMonitorCheckJob _checkJob;

        /// <summary>允许配置的请求方式</summary>
        private static readonly string[] AllowedMethods = ["GET", "POST", "HEAD"];

        public WebMonitorService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            WebMonitorCheckJob checkJob)
        {
            _fsql = fsql;
            _checkJob = checkJob;
        }

        public List<WebMonitorTargetDto> List()
            => _fsql.Select<WebMonitorTargetEntity>()
                .OrderByDescending(t => t.CreateTime)
                .ToList(t => new WebMonitorTargetDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Url = t.Url,
                    Method = t.Method,
                    ExpectStatus = t.ExpectStatus,
                    ExpectKeyword = t.ExpectKeyword,
                    TimeoutSeconds = t.TimeoutSeconds,
                    IntervalMinutes = t.IntervalMinutes,
                    Enabled = t.Enabled,
                    NotifyEmail = t.NotifyEmail,
                    LastStatus = t.LastStatus,
                    LastLatencyMs = t.LastLatencyMs,
                    LastErrorMsg = t.LastErrorMsg,
                    LastCheckAt = t.LastCheckAt,
                    Remark = t.Remark,
                });

        public int Save(WebMonitorTargetSaveDto dto)
        {
            Validate(dto);

            if (dto.Id is null or 0)
            {
                return (int)_fsql.Insert(new WebMonitorTargetEntity
                {
                    Name = dto.Name.Trim(),
                    Url = dto.Url.Trim(),
                    Method = dto.Method.ToUpperInvariant(),
                    ExpectStatus = dto.ExpectStatus,
                    ExpectKeyword = NullIfEmpty(dto.ExpectKeyword),
                    TimeoutSeconds = dto.TimeoutSeconds,
                    IntervalMinutes = dto.IntervalMinutes,
                    Enabled = dto.Enabled,
                    NotifyEmail = dto.NotifyEmail,
                    Remark = NullIfEmpty(dto.Remark),
                }).ExecuteIdentity();
            }

            var exists = _fsql.Select<WebMonitorTargetEntity>().Where(t => t.Id == dto.Id).Any();
            if (!exists) throw new NotFoundException("监控目标不存在");

            _fsql.Update<WebMonitorTargetEntity>()
                .Set(t => t.Name, dto.Name.Trim())
                .Set(t => t.Url, dto.Url.Trim())
                .Set(t => t.Method, dto.Method.ToUpperInvariant())
                .Set(t => t.ExpectStatus, dto.ExpectStatus)
                .Set(t => t.ExpectKeyword, NullIfEmpty(dto.ExpectKeyword))
                .Set(t => t.TimeoutSeconds, dto.TimeoutSeconds)
                .Set(t => t.IntervalMinutes, dto.IntervalMinutes)
                .Set(t => t.Enabled, dto.Enabled)
                .Set(t => t.NotifyEmail, dto.NotifyEmail)
                .Set(t => t.Remark, NullIfEmpty(dto.Remark))
                .Where(t => t.Id == dto.Id)
                .ExecuteAffrows();
            return dto.Id.Value;
        }

        public void Delete(int id)
        {
            _fsql.Delete<WebMonitorLogEntity>().Where(l => l.TargetId == id).ExecuteAffrows();
            var n = _fsql.Delete<WebMonitorTargetEntity>().Where(t => t.Id == id).ExecuteAffrows();
            if (n == 0) throw new NotFoundException("监控目标不存在");
        }

        public PagedResult<WebMonitorLogDto> GetLogs(int targetId, int page, int size)
        {
            if (page < 1) page = 1;
            if (size is < 1 or > 200) size = 20;

            var query = _fsql.Select<WebMonitorLogEntity>()
                .Where(l => l.TargetId == targetId)
                .OrderByDescending(l => l.CheckAt);
            return new PagedResult<WebMonitorLogDto>
            {
                Total = query.Count(),
                List = query.Page(page, size).ToList(l => new WebMonitorLogDto
                {
                    Id = l.Id,
                    Status = l.Status,
                    HttpStatusCode = l.HttpStatusCode,
                    LatencyMs = l.LatencyMs,
                    ErrorMsg = l.ErrorMsg,
                    CheckAt = l.CheckAt,
                })
            };
        }

        public async Task<WebMonitorLogDto> CheckNow(int id)
        {
            var target = _fsql.Select<WebMonitorTargetEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("监控目标不存在");

            var log = await _checkJob.CheckTargetAsync(target, notify: true);
            return new WebMonitorLogDto
            {
                Id = log.Id,
                Status = log.Status,
                HttpStatusCode = log.HttpStatusCode,
                LatencyMs = log.LatencyMs,
                ErrorMsg = log.ErrorMsg,
                CheckAt = log.CheckAt,
            };
        }

        /// <summary>监控健康度汇总：按最近探测状态计数 + 异常目标明细（首页数据看板用）</summary>
        public MonitorHealthDto GetHealth()
        {
            var targets = _fsql.Select<WebMonitorTargetEntity>().ToList();
            return new MonitorHealthDto
            {
                Total = targets.Count,
                EnabledCount = targets.Count(t => t.Enabled),
                OkCount = targets.Count(t => t.LastStatus == WebMonitorCheckJob.StatusOk),
                FailCount = targets.Count(t => t.LastStatus == WebMonitorCheckJob.StatusFail),
                PendingCount = targets.Count(t => t.LastStatus == null),
                FailedTargets = targets
                    .Where(t => t.LastStatus == WebMonitorCheckJob.StatusFail)
                    .OrderBy(t => t.LastCheckAt ?? DateTime.MaxValue)
                    .Select(t => new MonitorFailedItemDto
                    {
                        Id = t.Id,
                        Name = t.Name,
                        ErrorMsg = t.LastErrorMsg,
                        LastCheckAt = t.LastCheckAt,
                    })
                    .ToList(),
            };
        }

        /// <summary>保存前参数校验</summary>
        private static void Validate(WebMonitorTargetSaveDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("请输入监控目标名称");
            if (dto.Name.Trim().Length > 100)
                throw new BadRequestException("监控目标名称不能超过 100 字");
            if (string.IsNullOrWhiteSpace(dto.Url))
                throw new BadRequestException("请输入被监控地址");
            if (!Uri.TryCreate(dto.Url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new BadRequestException("被监控地址必须为有效的 http/https 链接");
            if (!AllowedMethods.Contains(dto.Method.ToUpperInvariant()))
                throw new BadRequestException("请求方式仅支持 GET/POST/HEAD");
            if (dto.ExpectStatus is < 100 or > 599)
                throw new BadRequestException("期望状态码必须在 100-599 之间");
            if (dto.TimeoutSeconds is < 1 or > 120)
                throw new BadRequestException("探测超时必须在 1-120 秒之间");
            if (dto.IntervalMinutes is < 1 or > 1440)
                throw new BadRequestException("探测间隔必须在 1-1440 分钟之间");
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
