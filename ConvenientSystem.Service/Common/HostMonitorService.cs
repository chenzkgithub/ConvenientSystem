using System.Collections.Concurrent;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Newtonsoft.Json;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 主机资源监控业务服务实现：目标配置管理 + 探测日志查询 + 手动立即检测（复用巡检 Job 的探测逻辑）
    /// </summary>
    public class HostMonitorService : IHostMonitorService
    {
        private readonly IFreeSql _fsql;
        private readonly HostMonitorCheckJob _checkJob;

        /// <summary>允许配置的指标类型</summary>
        private static readonly string[] AllowedMetrics =
            [HostMonitorMetrics.Disk, HostMonitorMetrics.Memory, HostMonitorMetrics.Cpu, HostMonitorMetrics.Service, HostMonitorMetrics.Host];

        public HostMonitorService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            HostMonitorCheckJob checkJob)
        {
            _fsql = fsql;
            _checkJob = checkJob;
        }

        // 先取实体再内存映射：IsLocal 需调本机地址识别方法，无法翻译成 SQL 投影
        public List<HostMonitorTargetDto> List()
            => _fsql.Select<HostMonitorTargetEntity>()
                .OrderByDescending(t => t.CreateTime)
                .ToList()
                .Select(t => new HostMonitorTargetDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    MetricType = t.MetricType,
                    HostAddress = t.HostAddress,
                    IsLocal = HostMonitorCheckJob.IsLocalAddress(t.HostAddress),
                    AuthAccount = t.AuthAccount,
                    MetricsJson = t.MetricsJson,
                    DriveLetter = t.DriveLetter,
                    ServiceNames = t.ServiceNames,
                    ThresholdPercent = t.ThresholdPercent,
                    TimeoutSeconds = t.TimeoutSeconds,
                    IntervalMinutes = t.IntervalMinutes,
                    Enabled = t.Enabled,
                    NotifyEmail = t.NotifyEmail,
                    LastStatus = t.LastStatus,
                    LastValue = t.LastValue,
                    LastErrorMsg = t.LastErrorMsg,
                    LastCheckAt = t.LastCheckAt,
                    Remark = t.Remark,
                }).ToList();

        public int Save(HostMonitorTargetSaveDto dto)
        {
            Validate(dto);

            if (dto.Id is null or 0)
            {
                return (int)_fsql.Insert(new HostMonitorTargetEntity
                {
                    Name = dto.Name.Trim(),
                    MetricType = dto.MetricType.ToUpperInvariant(),
                    HostAddress = NullIfEmpty(dto.HostAddress),
                    AuthAccount = NullIfEmpty(dto.AuthAccount),
                    AuthPassword = NullIfEmpty(dto.AuthPassword),
                    DriveLetter = NormalizeDrive(dto),
                    ServiceNames = NullIfEmpty(dto.ServiceNames),
                    ThresholdPercent = dto.ThresholdPercent,
                    TimeoutSeconds = dto.TimeoutSeconds,
                    IntervalMinutes = dto.IntervalMinutes,
                    Enabled = dto.Enabled,
                    NotifyEmail = dto.NotifyEmail,
                    Remark = NullIfEmpty(dto.Remark),
                }).ExecuteIdentity();
            }

            var exists = _fsql.Select<HostMonitorTargetEntity>().Where(t => t.Id == dto.Id).Any();
            if (!exists) throw new NotFoundException("监控目标不存在");

            var update = _fsql.Update<HostMonitorTargetEntity>()
                .Set(t => t.Name, dto.Name.Trim())
                .Set(t => t.MetricType, dto.MetricType.ToUpperInvariant())
                .Set(t => t.HostAddress, NullIfEmpty(dto.HostAddress))
                .Set(t => t.AuthAccount, NullIfEmpty(dto.AuthAccount))
                .Set(t => t.DriveLetter, NormalizeDrive(dto))
                .Set(t => t.ServiceNames, NullIfEmpty(dto.ServiceNames))
                .Set(t => t.ThresholdPercent, dto.ThresholdPercent)
                .Set(t => t.TimeoutSeconds, dto.TimeoutSeconds)
                .Set(t => t.IntervalMinutes, dto.IntervalMinutes)
                .Set(t => t.Enabled, dto.Enabled)
                .Set(t => t.NotifyEmail, dto.NotifyEmail)
                .Set(t => t.Remark, NullIfEmpty(dto.Remark))
                .Where(t => t.Id == dto.Id);
            // 编辑时密码留空 = 保留原密码；切换为本机目标时清空凭据
            if (!string.IsNullOrWhiteSpace(dto.AuthPassword))
                update = update.Set(t => t.AuthPassword, dto.AuthPassword.Trim());
            else if (string.IsNullOrWhiteSpace(dto.HostAddress))
                update = update.Set(t => t.AuthPassword, (string?)null);
            update.ExecuteAffrows();
            return dto.Id.Value;
        }

        public void Delete(int id)
        {
            _fsql.Delete<HostMonitorLogEntity>().Where(l => l.TargetId == id).ExecuteAffrows();
            var n = _fsql.Delete<HostMonitorTargetEntity>().Where(t => t.Id == id).ExecuteAffrows();
            if (n == 0) throw new NotFoundException("监控目标不存在");
        }

        public PagedResult<HostMonitorLogDto> GetLogs(int targetId, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            if (page < 1) page = 1;
            if (size is < 1 or > 200) size = 20;

            var query = _fsql.Select<HostMonitorLogEntity>()
                .Where(l => l.TargetId == targetId);
            query = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(l => l.CheckAt) : query.OrderByDynamic(sortField, sortOrder);
            return new PagedResult<HostMonitorLogDto>
            {
                Total = query.Count(),
                List = query.Page(page, size).ToList(l => new HostMonitorLogDto
                {
                    Id = l.Id,
                    Status = l.Status,
                    Value = l.Value,
                    ErrorMsg = l.ErrorMsg,
                    CheckAt = l.CheckAt,
                })
            };
        }

        public async Task<HostMonitorLogDto> CheckNow(int id)
        {
            var target = _fsql.Select<HostMonitorTargetEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("监控目标不存在");

            var log = await _checkJob.CheckTargetAsync(target, notify: true);
            return new HostMonitorLogDto
            {
                Id = log.Id,
                Status = log.Status,
                Value = log.Value,
                ErrorMsg = log.ErrorMsg,
                CheckAt = log.CheckAt,
            };
        }

        /// <summary>监控健康度汇总：按最近探测状态计数 + 异常目标明细（首页数据看板用）</summary>
        public MonitorHealthDto GetHealth()
        {
            var targets = _fsql.Select<HostMonitorTargetEntity>().ToList();
            return new MonitorHealthDto
            {
                Total = targets.Count,
                EnabledCount = targets.Count(t => t.Enabled),
                OkCount = targets.Count(t => t.LastStatus == HostMonitorCheckJob.StatusOk),
                FailCount = targets.Count(t => t.LastStatus == HostMonitorCheckJob.StatusFail),
                PendingCount = targets.Count(t => t.LastStatus == null),
                FailedTargets = targets
                    .Where(t => t.LastStatus == HostMonitorCheckJob.StatusFail)
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

        /// <summary>整机概览 Dashboard 数据：解析探测日志中的指标快照，返回最新快照 + 时间序列历史点</summary>
        public HostMetricsHistoryDto GetMetrics(int targetId, int hours)
        {
            if (hours is < 1 or > 168) hours = 6;
            var since = DateTime.Now.AddHours(-hours);

            var rows = _fsql.Select<HostMonitorLogEntity>()
                .Where(l => l.TargetId == targetId && l.CheckAt >= since && l.MetricsJson != null)
                .OrderBy(l => l.CheckAt)
                .ToList(l => new { l.CheckAt, l.MetricsJson });

            var result = new HostMetricsHistoryDto();
            foreach (var row in rows)
            {
                HostMetricsSnapshot? snap;
                try { snap = JsonConvert.DeserializeObject<HostMetricsSnapshot>(row.MetricsJson!); }
                catch { snap = null; }
                if (snap == null) continue;

                result.Latest = snap;
                result.History.Add(new HostMetricsPointDto
                {
                    CheckAt = row.CheckAt,
                    CpuPercent = snap.CpuPercent,
                    MemoryPercent = snap.MemoryPercent,
                    MemoryUsedGb = snap.MemoryUsedGb,
                    NetInKbps = snap.NetInKbps,
                    NetOutKbps = snap.NetOutKbps,
                    DiskReadMbPerSec = snap.DiskReadMbPerSec,
                    DiskWriteMbPerSec = snap.DiskWriteMbPerSec,
                });
            }
            return result;
        }

        /// <summary>磁盘扫描任务运行时状态（内存字典跟踪，前端轮询进度）</summary>
        private sealed class ScanJobState
        {
            public volatile bool Done;
            public string? Error;
            public int ScannedCount;
            public decimal FoundKb;
            public HostDiskScanDto? Result;
            public DateTime CreatedAt = DateTime.Now;
        }

        /// <summary>扫描任务字典（服务为单例，任务跨请求存活）；启动新任务时顺手清理过期项</summary>
        private readonly ConcurrentDictionary<string, ScanJobState> _scanJobs = new();

        /// <summary>启动磁盘扫描任务（后台异步执行）：立即返回 jobId，进度与结果由 GetScanProgress 轮询获取</summary>
        public string StartScan(int id, string? categories, string? drive)
        {
            var dto = ParseCategories(categories);
            dto.Drive = NormalizeDrive(drive);
            if (!dto.HasAny)
                throw new BadRequestException("请至少勾选一项清理内容");
            var target = GetHostTarget(id);

            // 顺手清理 10 分钟前的旧任务，避免内存字典无限增长
            foreach (var kv in _scanJobs)
            {
                if (kv.Value.Done && kv.Value.CreatedAt < DateTime.Now.AddMinutes(-10))
                    _scanJobs.TryRemove(kv.Key, out _);
            }

            var jobId = Guid.NewGuid().ToString("N");
            var state = new ScanJobState();
            _scanJobs[jobId] = state;
            // 后台执行扫描：PROGRESS 行实时写回状态，完成后置 Done；异常转为 Error 文案供前端提示
            _ = Task.Run(async () =>
            {
                try
                {
                    state.Result = await _checkJob.ScanDiskAsync(target, dto, (scanned, foundKb) =>
                    {
                        state.ScannedCount = scanned;
                        state.FoundKb = foundKb;
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
                {
                    state.Error = ex.Message;
                }
                catch (Exception ex)
                {
                    state.Error = "磁盘扫描失败：" + ex.Message;
                }
                finally
                {
                    state.Done = true;
                }
            });
            return jobId;
        }

        /// <summary>查询扫描任务进度/结果：未完成返回实时计数，完成后携带完整扫描结果或失败原因</summary>
        public HostDiskScanJobDto GetScanProgress(string jobId)
        {
            if (!_scanJobs.TryGetValue(jobId, out var state))
                throw new BadRequestException("扫描任务不存在或已过期，请重新扫描");
            return new HostDiskScanJobDto
            {
                JobId = jobId,
                Done = state.Done,
                ScannedCount = state.ScannedCount,
                FoundKb = state.FoundKb,
                Error = state.Error,
                Result = state.Done ? state.Result : null,
            };
        }

        /// <summary>磁盘清理任务运行时状态（与扫描任务同构，内存字典跟踪，前端轮询进度）</summary>
        private sealed class CleanJobState
        {
            public volatile bool Done;
            public string? Error;
            public int TotalCount;
            public int DeletedCount;
            public decimal FreedMb;
            public HostDiskCleanDto? Result;
            public DateTime CreatedAt = DateTime.Now;
        }

        /// <summary>清理任务字典（服务为单例，任务跨请求存活）；启动新任务时顺手清理过期项</summary>
        private readonly ConcurrentDictionary<string, CleanJobState> _cleanJobs = new();

        /// <summary>启动磁盘清理任务（后台异步执行）：校验后立即返回 jobId，进度与结果由 GetCleanProgress 轮询获取</summary>
        public string StartClean(int id, HostDiskCleanRequestDto dto)
        {
            dto.Drive = NormalizeDrive(dto.Drive);
            if (!dto.HasAny)
                throw new BadRequestException("请至少勾选一项清理内容");
            if (dto.Paths is { Count: > 0 }
                && !(dto.UserTemp || dto.WindowsTemp || dto.Prefetch || dto.UpdateCache
                    || dto.BrowserCache || dto.ThumbnailCache || dto.LogFiles || dto.OldDownloads || dto.DriveJunk))
                throw new BadRequestException("选定文件清理需勾选对应的清理分类");
            if (dto.RecyclePaths is { Count: > 0 } && !dto.RecycleBin)
                throw new BadRequestException("清理回收站条目需勾选回收站");
            var target = GetHostTarget(id);

            foreach (var kv in _cleanJobs)
            {
                if (kv.Value.Done && kv.Value.CreatedAt < DateTime.Now.AddMinutes(-10))
                    _cleanJobs.TryRemove(kv.Key, out _);
            }

            var jobId = Guid.NewGuid().ToString("N");
            var state = new CleanJobState
            {
                // 预计总数 = 勾选文件数 + 勾选回收站条目数（未传路径的整类清理时未知，前端改展示不确定进度）
                TotalCount = (dto.Paths?.Count ?? 0) + (dto.RecyclePaths?.Count ?? 0),
            };
            _cleanJobs[jobId] = state;
            // 后台执行清理：CPROG 行实时写回状态，完成后置 Done；异常转为 Error 文案供前端提示
            _ = Task.Run(async () =>
            {
                try
                {
                    state.Result = await _checkJob.CleanDiskAsync(target, dto, (deleted, freedMb) =>
                    {
                        state.DeletedCount = deleted;
                        state.FreedMb = freedMb;
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
                {
                    state.Error = ex.Message;
                }
                catch (Exception ex)
                {
                    state.Error = "磁盘清理失败：" + ex.Message;
                }
                finally
                {
                    state.Done = true;
                }
            });
            return jobId;
        }

        /// <summary>查询清理任务进度/结果：未完成返回实时计数，完成后携带完整清理结果或失败原因</summary>
        public HostDiskCleanJobDto GetCleanProgress(string jobId)
        {
            if (!_cleanJobs.TryGetValue(jobId, out var state))
                throw new BadRequestException("清理任务不存在或已过期，请重新操作");
            return new HostDiskCleanJobDto
            {
                JobId = jobId,
                Done = state.Done,
                TotalCount = state.TotalCount,
                DeletedCount = state.DeletedCount,
                FreedMb = state.FreedMb,
                Error = state.Error,
                Result = state.Done ? state.Result : null,
            };
        }

        /// <summary>采集整机概览目标的设备规格与 ipconfig /all 网络信息</summary>
        public async Task<HostSystemInfoDto> GetSystemInfo(int id)
        {
            var target = GetHostTarget(id);
            try
            {
                return await _checkJob.SystemInfoAsync(target);
            }
            catch (InvalidOperationException ex)
            {
                throw new BadRequestException(ex.Message);
            }
            catch (TimeoutException ex)
            {
                throw new BadRequestException(ex.Message);
            }
        }

        /// <summary>打开指定候选文件的所在文件夹（仅本机整机概览目标支持）</summary>
        public async Task OpenFolder(int id, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new BadRequestException("文件路径不能为空");
            var target = GetHostTarget(id);
            try
            {
                await _checkJob.OpenFolderAsync(target, path);
            }
            catch (InvalidOperationException ex)
            {
                throw new BadRequestException(ex.Message);
            }
        }

        /// <summary>打开系统回收站（仅本机整机概览目标支持）</summary>
        public async Task OpenRecycleBin(int id)
        {
            var target = GetHostTarget(id);
            try
            {
                await _checkJob.OpenRecycleBinAsync(target);
            }
            catch (InvalidOperationException ex)
            {
                throw new BadRequestException(ex.Message);
            }
        }

        /// <summary>提取文件扩展名对应的真实系统图标（与资源管理器展示一致）</summary>
        public async Task<Dictionary<string, string>> GetFileIcons(int id, string exts)
        {
            var target = GetHostTarget(id);
            var list = (exts ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return await _checkJob.GetFileIconsAsync(target, list);
        }

        /// <summary>加载整机概览监控目标（非 HOST 类型不支持磁盘操作）</summary>
        private HostMonitorTargetEntity GetHostTarget(int id)
        {
            var target = _fsql.Select<HostMonitorTargetEntity>().Where(t => t.Id == id).First()
                ?? throw new NotFoundException("监控目标不存在");
            if (!target.MetricType.Equals(HostMonitorMetrics.Host, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("仅整机概览目标支持磁盘扫描/清理");
            return target;
        }

        /// <summary>勾选分类码解析：userTemp/winTemp/prefetch/updateCache/browserCache/thumbnailCache/logFiles/oldDownloads/recycleBin</summary>
        private static HostDiskCleanRequestDto ParseCategories(string? categories)
        {
            var dto = new HostDiskCleanRequestDto();
            foreach (var c in (categories ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (c)
                {
                    case "userTemp": dto.UserTemp = true; break;
                    case "winTemp": dto.WindowsTemp = true; break;
                    case "prefetch": dto.Prefetch = true; break;
                    case "updateCache": dto.UpdateCache = true; break;
                    case "browserCache": dto.BrowserCache = true; break;
                    case "thumbnailCache": dto.ThumbnailCache = true; break;
                    case "logFiles": dto.LogFiles = true; break;
                    case "oldDownloads": dto.OldDownloads = true; break;
                    case "driveJunk": dto.DriveJunk = true; break;
                    case "recycleBin": dto.RecycleBin = true; break;
                }
            }
            return dto;
        }

        /// <summary>盘符归一化：去空白转大写，仅接受单个字母 A-Z（空则默认 C），非法时报 400</summary>
        private static string NormalizeDrive(string? drive)
        {
            var d = string.IsNullOrWhiteSpace(drive) ? "C" : drive.Trim().ToUpperInvariant();
            if (d.Length != 1 || d[0] < 'A' || d[0] > 'Z')
                throw new BadRequestException("盘符不合法，应为单个字母 A-Z");
            return d;
        }

        /// <summary>保存前参数校验</summary>
        private static void Validate(HostMonitorTargetSaveDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new BadRequestException("请输入监控目标名称");
            if (dto.Name.Trim().Length > 100)
                throw new BadRequestException("监控目标名称不能超过 100 字");
            if (!AllowedMetrics.Contains(dto.MetricType.ToUpperInvariant()))
                throw new BadRequestException("指标类型仅支持 磁盘/内存/CPU/服务/整机概览");
            if (!string.IsNullOrWhiteSpace(dto.HostAddress)
                && !dto.MetricType.Equals(HostMonitorMetrics.Host, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("目标电脑 IP 仅支持整机概览指标（一次采集 CPU/内存/磁盘等全部指标）");
            if (dto.HostAddress?.Trim().Length > 100)
                throw new BadRequestException("目标电脑 IP/主机名不能超过 100 字");
            if (!string.IsNullOrWhiteSpace(dto.HostAddress)
                && (string.IsNullOrWhiteSpace(dto.AuthAccount) || string.IsNullOrWhiteSpace(dto.AuthPassword)))
            {
                // 新增远程目标必须提供凭据；编辑时密码可留空（保留原密码），但账号必填
                var isNew = dto.Id is null or 0;
                if (string.IsNullOrWhiteSpace(dto.AuthAccount) || isNew)
                    throw new BadRequestException("远程目标必须填写采集账号与密码（目标电脑需开启 WinRM）");
            }
            if (dto.MetricType.Equals(HostMonitorMetrics.Service, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(dto.ServiceNames))
                throw new BadRequestException("服务监控必须填写服务名列表");
            if (dto.ThresholdPercent is < 0 or > 100)
                throw new BadRequestException("告警阈值必须在 0-100 之间");
            if (dto.TimeoutSeconds is < 5 or > 120)
                throw new BadRequestException("探测超时必须在 5-120 秒之间");
            if (dto.IntervalMinutes is < 1 or > 1440)
                throw new BadRequestException("探测间隔必须在 1-1440 分钟之间");
        }

        /// <summary>盘符规范化：仅磁盘指标保留单字母盘符，其余置空</summary>
        private static string? NormalizeDrive(HostMonitorTargetSaveDto dto)
        {
            if (!dto.MetricType.Equals(HostMonitorMetrics.Disk, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(dto.DriveLetter))
                return null;
            var drive = dto.DriveLetter.Trim().TrimEnd(':').ToUpperInvariant();
            if (drive.Length != 1 || !char.IsLetter(drive[0]))
                throw new BadRequestException("磁盘盘符必须为单个字母（如 C）");
            return drive;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
